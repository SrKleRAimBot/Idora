/**
 * Idora.JS — prefixed, time-ordered entity identifiers such as `usr_01k2f9v8ta7m3x0qbrn4dzeh5c`.
 *
 * @packageDocumentation
 */

export { Idora } from './idora.js';
export { entityPrefix } from './entity-prefix.js';
export { EntityPrefixRegistry } from './registry.js';
export {
  DuplicateEntityPrefixError,
  EntityPrefixNotRegisteredError,
  IdoraError,
  InvalidEntityPrefixError,
  type InvalidPrefixReason,
} from './errors.js';
export type { EntityModule, EntityRegistration, EntityType, IdoraOptions, IdStrategy } from './types.js';
