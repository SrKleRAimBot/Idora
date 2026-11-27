/**
 * idora.js-zod — Zod schemas for Idora identifiers such as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.
 *
 * @packageDocumentation
 */

import { EntityPrefixRegistry, type EntityType, type Id, Idora, IdoraId, InvalidIdError, type InvalidIdReason } from 'idora.js';
import * as z from 'zod';

/** What to say when an identifier is rejected: a fixed message, or one built from the error. */
export type IdoraIdErrorMessage = string | ((error: InvalidIdError) => string);

/** Accept identifiers with any prefix, or with exactly the one given. */
export interface PrefixOptions<P extends string = string> {
  /** The prefix every identifier must carry, compared exactly, case included. Omit to accept any. */
  readonly prefix?: P;
  readonly entity?: undefined;
  readonly registry?: undefined;
  /** Replaces the default issue message. See {@link IdoraIdErrorMessage}. */
  readonly error?: IdoraIdErrorMessage;
}

/** Accept identifiers with the prefix a registered entity type was given. */
export interface EntityOptions {
  /** The entity type whose identifiers are accepted. */
  readonly entity: EntityType;
  /** The generator, or its registry, that `entity` is registered with. */
  readonly registry: Idora | EntityPrefixRegistry;
  readonly prefix?: undefined;
  /** Replaces the default issue message. See {@link IdoraIdErrorMessage}. */
  readonly error?: IdoraIdErrorMessage;
}

/** Which identifiers a schema accepts, and what it says about the rest. */
export type IdoraIdSchemaOptions<P extends string = string> = PrefixOptions<P> | EntityOptions;

/**
 * The `params` of the issue a rejected identifier raises, so that code handling a `ZodError` can
 * tell why without matching on the message.
 */
export interface IdoraIdIssueParams {
  /** Which check the identifier failed — see `InvalidIdError` in `idora.js`. */
  readonly reason: InvalidIdReason;
  /** The prefix the schema expected, when it expected one. */
  readonly expectedPrefix: string | undefined;
}

/**
 * A string schema that accepts Idora identifiers, optionally with one particular prefix, and types
 * what it accepts as {@link Id}.
 *
 * ```ts
 * import { idoraId } from 'idora.js-zod';
 * import * as z from 'zod';
 *
 * const Params = z.object({
 *   userId: idoraId({ prefix: 'usr' }),               // Id<'usr'>
 *   orderId: idoraId({ entity: Order, registry: ids }), // the prefix Order was registered with
 *   ref: idoraId(),                                    // any well-formed identifier
 * });
 * ```
 *
 * The output is the input string itself — parsing is strict, so a valid identifier is never
 * rewritten — branded as `Id<P>` so that the type checker knows it has been checked. Use
 * {@link parsedIdoraId} to decode into an `IdoraId` instead.
 *
 * A rejected identifier raises one `custom` issue whose message says what is wrong, and whose
 * `params` carry the {@link IdoraIdIssueParams | reason}. Anything that is not a string is rejected
 * by `z.string()` as usual.
 *
 * @throws {EntityPrefixNotRegisteredError} `entity` is not registered with `registry`. Thrown when
 * the schema is built, so the mistake surfaces at start-up rather than on the first request.
 * @throws {InvalidEntityPrefixError} `prefix` is not itself a valid prefix.
 * @throws {TypeError} The options are contradictory, such as an `entity` without a `registry`.
 */
export function idoraId<P extends string = string>(options: IdoraIdSchemaOptions<P> = {}): z.ZodType<Id<P>, string> {
  const expectation = resolve(options);
  const message = options.error;

  // The output is the very string that came in; only its static type changes, so the cast is the
  // whole of the transformation and the schema stays a plain string schema — which keeps it usable
  // in JSON Schema output, and in both directions of a codec.
  return z.string().check((payload) => {
    if (IdoraId.isValid(payload.value, expectation.prefix)) {
      return;
    }

    const error = expectation.explain(payload.value);
    const params: IdoraIdIssueParams = { reason: error.reason, expectedPrefix: error.expectedPrefix };
    payload.issues.push({
      code: 'custom',
      message: message === undefined ? defaultMessage(error) : typeof message === 'string' ? message : message(error),
      input: payload.value,
      params,
    });
  }) as unknown as z.ZodType<Id<P>, string>;
}

/**
 * A codec that accepts the same identifiers as {@link idoraId} and decodes them into parsed
 * `IdoraId` objects — and, with `z.encode`, turns `IdoraId` objects back into strings.
 *
 * ```ts
 * import { parsedIdoraId } from 'idora.js-zod';
 *
 * const Query = z.object({ after: parsedIdoraId({ prefix: 'evt' }) });
 *
 * const { after } = Query.parse(request.query);
 * after.createdAt;                    // when the event was minted
 * z.encode(Query, { after });         // { after: 'evt_01k2…' }
 * ```
 *
 * @throws {EntityPrefixNotRegisteredError} `entity` is not registered with `registry`.
 * @throws {InvalidEntityPrefixError} `prefix` is not itself a valid prefix.
 * @throws {TypeError} The options are contradictory, such as an `entity` without a `registry`.
 */
export function parsedIdoraId<P extends string = string>(
  options: IdoraIdSchemaOptions<P> = {},
): z.ZodCodec<z.ZodType<Id<P>, string>, z.ZodCustom<IdoraId<P>, IdoraId<P>>> {
  const parsed = z.custom<IdoraId<P>>((value) => value instanceof IdoraId, 'Invalid input: expected an IdoraId');
  return z.codec(idoraId(options), parsed, {
    // The input schema has already checked the text, prefix included, so this cannot throw.
    decode: (text) => IdoraId.parse(text) as IdoraId<P>,
    // And on the way back out, the input schema checks the text again — so encoding an IdoraId with
    // the wrong prefix fails just as decoding one would.
    encode: (id) => id.text,
  });
}

/** What a schema expects, resolved once when it is built. */
interface Expectation {
  /** The prefix identifiers must carry, if any. */
  readonly prefix: string | undefined;
  /** Works out why `text` was rejected. Called only for text already known to be invalid. */
  readonly explain: (text: string) => InvalidIdError;
}

function resolve(options: IdoraIdSchemaOptions): Expectation {
  if (options === null || typeof options !== 'object') {
    throw new TypeError(`The options for an identifier schema must be an object, but received ${options === null ? 'null' : typeof options}.`);
  }

  const { prefix, entity, registry } = options as { prefix?: unknown; entity?: unknown; registry?: unknown };

  if (entity === undefined) {
    if (registry !== undefined) {
      throw new TypeError(`An identifier schema was given a registry but no entity. Name the entity type too, as in idoraId({ entity: User, registry: ids }).`);
    }

    if (prefix !== undefined) {
      // Validates the prefix now, rather than on the first identifier the schema sees.
      IdoraId.isValid('', prefix as string);
    }

    return { prefix: prefix as string | undefined, explain: (text) => failure(() => IdoraId.parse(text, prefix as string | undefined)) };
  }

  if (prefix !== undefined) {
    throw new TypeError(`An identifier schema was given both a prefix and an entity. Give one: the entity's registered prefix is used when you name the entity.`);
  }

  const prefixes = registry instanceof Idora ? registry.registry : registry;
  if (!(prefixes instanceof EntityPrefixRegistry)) {
    throw new TypeError(`An identifier schema was given an entity but no registry to look its prefix up in. Pass the generator too, as in idoraId({ entity: User, registry: ids }).`);
  }

  const entityType = entity as EntityType;
  return { prefix: prefixes.getPrefix(entityType), explain: (text) => failure(() => prefixes.parse(entityType, text)) };
}

/** Runs a parse already known to fail, and returns the error it throws. */
function failure(parse: () => unknown): InvalidIdError {
  try {
    parse();
  } catch (error) {
    if (error instanceof InvalidIdError) {
      return error;
    }

    throw error;
  }

  throw new Error('idora.js-zod internal error: an identifier was rejected but parses.');
}

/** Short, user-facing messages in Zod's own style, for the reasons an identifier is rejected. */
function defaultMessage(error: InvalidIdError): string {
  switch (error.reason) {
    case 'noSeparator':
      return `Invalid identifier: expected a prefix and a value separated by '_', such as ${error.expectedPrefix ?? 'usr'}_01k2f9v8ta7m3x0qbrn4dzeh5c`;
    case 'prefix':
      return `Invalid identifier: the prefix before '_' must be 1 to 16 ASCII letters or digits`;
    case 'value':
      return `Invalid identifier: the value after '_' must be a lowercase ULID or UUIDv7`;
    case 'prefixMismatch':
      return `Invalid identifier: expected the prefix '${error.expectedPrefix}', received '${error.text.slice(0, error.text.indexOf('_'))}'`;
  }
}
