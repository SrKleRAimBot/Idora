import type { EntityType } from './types.js';

/** Prefixes declared with {@link entityPrefix}, keyed by the exact class that carries them. */
const declaredPrefixes = new WeakMap<EntityType, unknown>();

/**
 * Declares, on the class itself, the prefix its identifiers should carry.
 *
 * ```ts
 * @entityPrefix('usr')
 * export class User {
 *   constructor(readonly id: string) {}
 * }
 *
 * const ids = new Idora({ entities: [User] });                 // name the class, or
 * const ids = new Idora({ modules: [await import('./entities.js')] }); // name its module
 *
 * ids.generate(User); // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
 * ```
 *
 * The decorator on its own does nothing but record the prefix. It is collected when you name the
 * class in {@link IdoraOptions.entities}, or a module exporting it in {@link IdoraOptions.modules};
 * Idora never goes looking for decorated classes by itself.
 *
 * Works with both standard (TC39) decorators and TypeScript's `experimentalDecorators`. Without a
 * compiler, call it as a function — `entityPrefix('usr')(User)` — to the same effect.
 *
 * The declaration is **not inherited**: a subclass of `User` does not quietly take `usr`, which
 * would make it collide with `User` the moment both were registered. Decorate the subclass itself
 * if it needs a prefix.
 *
 * The prefix is validated when the class is registered rather than here, with a message that names
 * the class carrying the mistake. Decorating one class twice throws immediately.
 *
 * @param prefix 1 to 16 ASCII letters or digits. Lowercase reads best, but the prefix is used
 * exactly as written.
 */
export function entityPrefix(prefix: string) {
  return function declareEntityPrefix<T extends EntityType>(target: T, _context?: ClassDecoratorContext<T>): void {
    if (declaredPrefixes.has(target)) {
      throw new TypeError(
        `${nameOf(target)} already declares an entity prefix with @entityPrefix. A class may declare only one.`,
      );
    }

    declaredPrefixes.set(target, prefix);
  };
}

/**
 * Returns whether `entityType` itself — not a superclass — carries an {@link entityPrefix}
 * declaration, and if so, what it declared.
 */
export function declaredPrefixOf(entityType: EntityType): { readonly declared: boolean; readonly prefix: unknown } {
  return declaredPrefixes.has(entityType)
    ? { declared: true, prefix: declaredPrefixes.get(entityType) }
    : { declared: false, prefix: undefined };
}

/** A class's name for messages, which an anonymous class does not have. */
export function nameOf(entityType: EntityType): string {
  return entityType.name === '' ? '<anonymous class>' : entityType.name;
}
