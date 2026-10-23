import { entityPrefix } from '../../src/index.js';

/** Declares a prefix containing the separator, which must be reported against this class by name. */
@entityPrefix('bad_prefix')
export class MalformedEntity {}
