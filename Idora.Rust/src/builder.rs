//! Configuration: the value format and the registry (specification §4).

#![forbid(unsafe_code)]

use core::any::{TypeId, type_name};
use std::collections::HashMap;

use crate::entity::{Declaration, Entity, EntitySet};
use crate::error::{DuplicatePrefixError, Error, InvalidPrefixError, TypeInfo};
use crate::generator::{Idora, Registration, TypeIdMap};
use crate::prefix::{self, MAX_PREFIX_LEN};
use crate::strategy::Strategy;

/// Configures and builds an [`Idora`] generator.
///
/// Every registration is validated as it is made, so a mistake surfaces at start-up on the line
/// that caused it, not as a puzzling identifier in production. A failed registration changes
/// nothing; everything registered before it is kept.
///
/// The methods take `&mut self` and return `&mut Self`, like [`std::process::Command`], so they
/// chain in one expression and also work one statement at a time:
///
/// ```
/// use idora::{Entity, Idora};
///
/// struct User;
/// struct Order;
/// struct Invoice;
/// impl Entity for Invoice {
///     const PREFIX: &'static str = "inv";
/// }
///
/// // Chained.
/// let ids = Idora::builder()
///     .register::<User>("usr")?
///     .register::<Order>("ord")?
///     .register_entity::<Invoice>()?
///     .build();
///
/// // One step at a time, for conditional registration.
/// # let billing_enabled = true;
/// let mut builder = Idora::builder();
/// builder.uuid_v7();
/// builder.register::<User>("usr")?;
/// if billing_enabled {
///     builder.register_entity::<Invoice>()?;
/// }
/// let uuid_ids = builder.build();
/// # let _ = (ids, uuid_ids);
/// # Ok::<(), idora::Error>(())
/// ```
#[derive(Clone, Debug, Default)]
pub struct Builder {
    strategy: Strategy,
    registrations: Vec<Registration>,
    by_type: TypeIdMap<usize>,
    /// The reverse lookup that detects collisions, keyed by the ASCII-lowercased prefix so that
    /// `usr` and `USR` collide.
    by_folded_prefix: HashMap<FoldedPrefix, usize>,
}

/// A validated prefix lowercased into a fixed array, so the collision check never allocates.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
struct FoldedPrefix([u8; MAX_PREFIX_LEN], u8);

impl FoldedPrefix {
    /// `prefix` must already be valid: at most 16 ASCII bytes.
    fn new(prefix: &str) -> Self {
        let mut folded = [0u8; MAX_PREFIX_LEN];
        for (slot, byte) in folded.iter_mut().zip(prefix.bytes()) {
            *slot = byte.to_ascii_lowercase();
        }
        #[allow(clippy::cast_possible_truncation)] // at most 16
        Self(folded, prefix.len() as u8)
    }
}

impl Builder {
    /// Creates a builder with the default [`Strategy::Ulid`] and nothing registered. Same as
    /// [`Idora::builder`].
    #[must_use]
    pub fn new() -> Self {
        Self::default()
    }

    /// Chooses the value format. The default is [`Strategy::Ulid`].
    pub fn strategy(&mut self, strategy: Strategy) -> &mut Self {
        self.strategy = strategy;
        self
    }

    /// Uses a 26-character lowercase ULID as the value. This is already the default; call it when
    /// you would rather the choice were written down than implied.
    pub fn ulid(&mut self) -> &mut Self {
        self.strategy(Strategy::Ulid)
    }

    /// Uses a 32-character lowercase UUIDv7 as the value instead of the default ULID.
    ///
    /// Worth doing when a column, schema or downstream service insists on a real UUID. Otherwise
    /// the ULID default is six characters shorter for the same 128 bits.
    pub fn uuid_v7(&mut self) -> &mut Self {
        self.strategy(Strategy::UuidV7)
    }

    /// Associates `T` with the prefix its identifiers carry.
    ///
    /// `T` is used purely as a key: it needs no trait, and can be an entity struct, an enum, a
    /// marker type, even `dyn Trait`. The prefix must be 1 to 16 ASCII letters or digits and is
    /// stored exactly as written.
    ///
    /// # Errors
    ///
    /// - [`Error::InvalidPrefix`] if `prefix` is empty, longer than 16 characters, or contains
    ///   anything but ASCII letters and digits.
    /// - [`Error::DuplicatePrefix`] if `T` is already registered (even with the same prefix), or
    ///   `prefix` already belongs to another type, ignoring ASCII case.
    ///
    /// ```
    /// struct User;
    /// let ids = idora::Idora::builder().register::<User>("usr")?.build();
    /// assert!(ids.generate::<User>()?.starts_with("usr_"));
    /// # Ok::<(), idora::Error>(())
    /// ```
    pub fn register<T: ?Sized + 'static>(&mut self, prefix: &str) -> Result<&mut Self, Error> {
        let entity = TypeInfo {
            id: TypeId::of::<T>(),
            name: type_name::<T>(),
        };
        self.insert(entity, prefix, false)?;
        Ok(self)
    }

    /// Registers `T` with the prefix it declares in [`Entity::PREFIX`].
    ///
    /// # Errors
    ///
    /// As [`register`](Self::register). An invalid declared prefix is reported with
    /// [`InvalidPrefixError::declaring_type_name`] set, and a message naming `T`.
    pub fn register_entity<T: Entity>(&mut self) -> Result<&mut Self, Error> {
        self.register_declarations(&mut [Declaration::of::<T>()])?;
        Ok(self)
    }

    /// Registers every type in a tuple of [`Entity`] types, such as `(User, Order, Invoice)`.
    ///
    /// The types are registered in ordinal order of their [`std::any::type_name`], not the order
    /// they are listed in, so a clash between two of them is reported the same way round however
    /// the tuple is written. Registration stops at the first failure; the types before it stay
    /// registered.
    ///
    /// You list the types rather than the crate discovering them. Rust has no run-time reflection
    /// to discover them with, and that is a feature: an explicit list cannot silently miss a type,
    /// costs nothing at start-up, and stops unrelated code registering prefixes into your
    /// application.
    ///
    /// # Errors
    ///
    /// As [`register_entity`](Self::register_entity), for the first type that fails.
    ///
    /// ```
    /// use idora::{Entity, Idora};
    ///
    /// struct User;
    /// struct Order;
    /// impl Entity for User { const PREFIX: &'static str = "usr"; }
    /// impl Entity for Order { const PREFIX: &'static str = "ord"; }
    ///
    /// let ids = Idora::builder().register_entities::<(User, Order)>()?.build();
    /// assert_eq!(ids.registrations().len(), 2);
    /// # Ok::<(), idora::Error>(())
    /// ```
    pub fn register_entities<S: EntitySet>(&mut self) -> Result<&mut Self, Error> {
        S::__register_into(self)?;
        Ok(self)
    }

    /// Builds the generator. The builder is left as it was, so it can build again.
    #[must_use]
    pub fn build(&self) -> Idora {
        Idora::from_parts(
            self.strategy,
            self.registrations.clone(),
            self.by_type.clone(),
        )
    }

    /// Sorts declarations by type name and registers them in that order.
    pub(crate) fn register_declarations(
        &mut self,
        declarations: &mut [Declaration],
    ) -> Result<(), Error> {
        // Stable, so two distinct types that happen to share a name keep the order they were
        // listed in.
        declarations.sort_by(|a, b| a.entity.name.cmp(b.entity.name));
        for declaration in declarations.iter() {
            self.insert(declaration.entity, declaration.prefix, true)?;
        }
        Ok(())
    }

    /// The single place where prefixes are validated and both kinds of collision detected, so
    /// explicit and declarative registrations police each other in either order.
    fn insert(&mut self, entity: TypeInfo, prefix: &str, declared: bool) -> Result<(), Error> {
        if let Err(error) = prefix::validate_prefix(prefix) {
            let error: InvalidPrefixError = if declared {
                error.declared_by(entity.name)
            } else {
                error
            };
            return Err(error.into());
        }

        // Checked first: "you already registered this type" is the more useful message when
        // both problems are true.
        if let Some(&index) = self.by_type.get(&entity.id) {
            let existing = self.registrations[index].prefix();
            return Err(DuplicatePrefixError::type_registered(entity, existing, prefix).into());
        }

        let folded = FoldedPrefix::new(prefix);
        if let Some(&index) = self.by_folded_prefix.get(&folded) {
            let holder = &self.registrations[index];
            let conflicting = TypeInfo {
                id: holder.type_id(),
                name: holder.type_name(),
            };
            return Err(DuplicatePrefixError::prefix_taken(
                prefix,
                entity,
                holder.prefix(),
                conflicting,
            )
            .into());
        }

        let index = self.registrations.len();
        self.registrations
            .push(Registration::new(entity.id, entity.name, prefix));
        self.by_type.insert(entity.id, index);
        self.by_folded_prefix.insert(folded, index);
        Ok(())
    }
}
