import { entityPrefix } from '../../src/index.js';

/*
 * Two decorated classes competing for one prefix (in different case), exported in reverse name
 * order, to prove the scan reports the clash the same way round whatever order exports appear in.
 */

@entityPrefix('ZCLASH')
export class Zebra {}

@entityPrefix('zclash')
export class Aardvark {}
