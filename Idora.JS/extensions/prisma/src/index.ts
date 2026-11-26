/**
 * idora.js-prisma — a Prisma Client extension that gives new rows Idora identifiers such as
 * `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.
 *
 * @packageDocumentation
 */

import { Prisma } from '@prisma/client/extension';
import { type EntityType, Idora } from 'idora.js';

/**
 * Where a model's identifiers come from:
 *
 * - an entity type registered with the generator, as in `user: User`;
 * - a prefix, as in `auditLog: 'aud'` — for the many Prisma projects whose models have no class
 *   of their own to register;
 * - either of those with a `field` other than `id`, as in `session: { prefix: 'ses', field: 'token' }`.
 */
export type ModelIdSource =
  | EntityType
  | string
  | { readonly entity: EntityType; readonly prefix?: undefined; readonly field?: string }
  | { readonly prefix: string; readonly entity?: undefined; readonly field?: string };

/** Configuration for {@link idoraExtension}. Read once, when the extension is created. */
export interface IdoraExtensionOptions {
  /** The generator that mints the identifiers, and with which any entity types are registered. */
  readonly ids: Idora;

  /**
   * The models that get identifiers, keyed by name — either as the client spells it (`user`,
   * `auditLog`) or as the schema does (`User`, `AuditLog`). Models not listed are left alone.
   */
  readonly models: Readonly<Record<string, ModelIdSource>>;
}

/** How one model's missing identifiers are filled in. */
interface ModelIds {
  readonly field: string;
  readonly generate: () => string;
}

/**
 * A Prisma Client extension that fills in a model's identifier whenever a create leaves it out:
 *
 * ```ts
 * import { PrismaClient } from './generated/prisma/client.js';
 * import { idoraExtension } from 'idora.js-prisma';
 *
 * export const prisma = new PrismaClient({ adapter }).$extends(
 *   idoraExtension({
 *     ids,
 *     models: {
 *       user: User,       // the prefix User is registered with
 *       auditLog: 'aud',  // or a prefix of its own
 *     },
 *   }),
 * );
 *
 * const user = await prisma.user.create({ data: { email } });
 * user.id; // 'usr_01k2f9v8ta7m3x0qbrn4dzeh5c'
 * ```
 *
 * In the schema, the field is a plain string with no `@default`: the identifier is generated in
 * your process, by the same generator as the rest of your application, before the query is sent.
 *
 * ```prisma
 * model User {
 *   id    String @id @db.VarChar(30)
 *   email String @unique
 * }
 * ```
 *
 * `create`, `createMany`, `createManyAndReturn` and the create half of `upsert` are covered. An
 * identifier the query already supplies is kept. Prisma does not run query extensions for the
 * records a nested write creates — `posts: { create: [...] }` inside a `user.create` — so give
 * those an identifier yourself, with `ids.generate(Post)`.
 *
 * The arguments you pass are never modified; the query receives a copy with the identifiers added.
 *
 * @throws {EntityPrefixNotRegisteredError} A model names an entity type that is not registered with
 * `ids`. Thrown when the extension is created, so the mistake stops start-up.
 * @throws {InvalidEntityPrefixError} A model names a prefix that is not valid.
 * @throws {TypeError} The options have the wrong shape, or two keys name the same model.
 */
export function idoraExtension(options: IdoraExtensionOptions): ReturnType<typeof Prisma.defineExtension> {
  const models = resolveModels(options);
  const lookup = (model: string): ModelIds | undefined => models.get(clientName(model));

  return Prisma.defineExtension({
    name: 'idora',
    query: {
      $allModels: {
        create({ model, args, query }) {
          const ids = lookup(model);
          return query(ids === undefined ? args : { ...args, data: fillOne(ids, args['data']) });
        },
        createMany({ model, args, query }) {
          const ids = lookup(model);
          return query(ids === undefined ? args : { ...args, data: fillMany(ids, args['data']) });
        },
        createManyAndReturn({ model, args, query }) {
          const ids = lookup(model);
          return query(ids === undefined ? args : { ...args, data: fillMany(ids, args['data']) });
        },
        upsert({ model, args, query }) {
          const ids = lookup(model);
          return query(ids === undefined ? args : { ...args, create: fillOne(ids, args['create']) });
        },
      },
    },
  });
}

/** Checks the configuration and works out, for each model, which field to fill and how. */
function resolveModels(options: IdoraExtensionOptions): ReadonlyMap<string, ModelIds> {
  if (options === null || typeof options !== 'object') {
    throw new TypeError(`idoraExtension needs an options object, as in idoraExtension({ ids, models: { user: User } }).`);
  }

  const { ids, models } = options;
  if (!(ids instanceof Idora)) {
    throw new TypeError(`idoraExtension needs the Idora generator as 'ids', as in idoraExtension({ ids, models: { user: User } }).`);
  }

  if (models === null || typeof models !== 'object') {
    throw new TypeError(`idoraExtension needs 'models', mapping model names to entity types or prefixes, as in { user: User }.`);
  }

  const resolved = new Map<string, ModelIds>();
  for (const [key, source] of Object.entries(models)) {
    const name = clientName(key);
    if (resolved.has(name)) {
      throw new TypeError(`The models '${key}' and '${name}' given to idoraExtension are the same model. List each model once.`);
    }

    resolved.set(name, resolveModel(ids, key, source));
  }

  return resolved;
}

function resolveModel(ids: Idora, key: string, source: ModelIdSource): ModelIds {
  const { entity, prefix, field = 'id' } =
    typeof source === 'function' ? { entity: source } : typeof source === 'string' ? { prefix: source } : (source ?? {});

  if (typeof field !== 'string' || field === '') {
    throw new TypeError(`The field given for the model '${key}' must be the name of a field, such as 'id'.`);
  }

  if (entity !== undefined) {
    if (prefix !== undefined) {
      throw new TypeError(`The model '${key}' was given both an entity and a prefix. Give one: the entity's registered prefix is used when you name the entity.`);
    }

    // Throws now, for an unregistered entity, rather than on the first insert.
    ids.registry.getPrefix(entity);
    return { field, generate: () => ids.generate(entity) };
  }

  if (prefix === undefined) {
    throw new TypeError(`The model '${key}' needs an entity type registered with the generator, or a prefix, as in { ${key}: User } or { ${key}: 'usr' }.`);
  }

  // Generating one identifier validates the prefix exactly as every later call will, so a typo
  // stops start-up rather than the first insert.
  ids.generateWithPrefix(prefix);
  return { field, generate: () => ids.generateWithPrefix(prefix) };
}

/** A copy of one record's data with the identifier filled in, if it was missing. */
function fillOne<T>(ids: ModelIds, data: T): T {
  if (data === null || typeof data !== 'object' || Array.isArray(data)) {
    // Not something this extension understands; let Prisma report it.
    return data;
  }

  const record = data as Record<string, unknown>;
  return record[ids.field] === undefined ? ({ ...record, [ids.field]: ids.generate() } as T) : data;
}

/** {@link fillOne} for `createMany`, whose `data` may be one record or a list of them. */
function fillMany<T>(ids: ModelIds, data: T): T {
  return Array.isArray(data) ? (data.map((record: unknown) => fillOne(ids, record)) as T) : fillOne(ids, data);
}

/**
 * The name a model goes by on the client: the schema's name with its first letter lowercased, so
 * `AuditLog` and `auditLog` are the same model. Query hooks are told the schema's name.
 */
function clientName(model: string): string {
  return model.charAt(0).toLowerCase() + model.slice(1);
}
