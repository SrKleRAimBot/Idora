import assert from 'node:assert/strict';
import { describe, it } from 'node:test';

import { EntityPrefixNotRegisteredError, type Id, Idora, IdoraId, InvalidEntityPrefixError } from 'idora.js';
import * as z from 'zod';

import { type IdoraIdIssueParams, idoraId, parsedIdoraId } from '../src/index.js';

class User {}
class Order {}
class Unregistered {}

const ids = new Idora({ entities: [[User, 'usr'], [Order, 'ord']] });

const USER_ID = 'usr_01k226e0g0008j4ct4ank7f24s';
const ORDER_ID = 'ord_0198846702007011a233445566778899';

/** The single issue a failed parse raised. */
function onlyIssue(result: z.ZodSafeParseResult<unknown>): z.core.$ZodIssue {
  assert.equal(result.success, false, 'expected the parse to fail');
  assert.equal(result.error!.issues.length, 1, JSON.stringify(result.error!.issues));
  return result.error!.issues[0]!;
}

function paramsOf(issue: z.core.$ZodIssue): IdoraIdIssueParams {
  assert.equal(issue.code, 'custom');
  return (issue as z.core.$ZodIssueCustom).params as IdoraIdIssueParams;
}

describe('idoraId()', () => {
  it('accepts a well-formed identifier with any prefix, in either format, unchanged', () => {
    // Arrange
    const schema = idoraId();

    // Act & Assert
    assert.equal(schema.parse(USER_ID), USER_ID);
    assert.equal(schema.parse(ORDER_ID), ORDER_ID);
    assert.equal(schema.parse(ids.generate(User)).slice(0, 4), 'usr_');
  });

  it('types its output as a branded Id and its input as a string', () => {
    // Arrange
    const schema = z.object({ id: idoraId({ prefix: 'usr' }) });

    // Act
    const output: z.output<typeof schema> = schema.parse({ id: USER_ID });

    // Assert
    const id: Id<'usr'> = output.id;
    const input: z.input<typeof schema> = { id: 'any string will type-check' };
    // @ts-expect-error -- the output is not an Id for another prefix.
    const wrong: Id<'ord'> = output.id;
    assert.equal(id, USER_ID);
    assert.equal(typeof input.id, 'string');
    assert.equal(wrong, USER_ID);
  });

  const REJECTED: ReadonlyArray<readonly [text: string, reason: string, message: RegExp]> = [
    ['nope', 'noSeparator', /^Invalid identifier: expected a prefix and a value separated by '_', such as usr_01k2f9v8ta7m3x0qbrn4dzeh5c$/],
    ['us-r_01k226e0g0008j4ct4ank7f24s', 'prefix', /^Invalid identifier: the prefix before '_' must be 1 to 16 ASCII letters or digits$/],
    ['usr_01K226E0G0008J4CT4ANK7F24S', 'value', /^Invalid identifier: the value after '_' must be a lowercase ULID or UUIDv7$/],
    [` ${USER_ID}`, 'prefix', /prefix before '_'/],
    [`${USER_ID} `, 'value', /value after '_'/],
  ];

  for (const [text, reason, message] of REJECTED) {
    it(`rejects ${JSON.stringify(text)} with one issue, for the reason ${reason}`, () => {
      // Act
      const issue = onlyIssue(idoraId().safeParse(text));

      // Assert
      assert.match(issue.message, message);
      assert.deepEqual(paramsOf(issue), { reason, expectedPrefix: undefined });
      assert.deepEqual(issue.path, []);
    });
  }

  it('rejects anything that is not a string as Zod always does', () => {
    // Act
    const issue = onlyIssue(idoraId().safeParse(42));

    // Assert
    assert.equal(issue.code, 'invalid_type');
  });

  it('places the issue at the path of the field that failed', () => {
    // Arrange
    const schema = z.object({ order: z.object({ ids: z.array(idoraId()) }) });

    // Act
    const issue = onlyIssue(schema.safeParse({ order: { ids: [USER_ID, 'nope'] } }));

    // Assert
    assert.deepEqual(issue.path, ['order', 'ids', 1]);
  });

  it('composes with optional, nullable and async parsing', async () => {
    // Arrange
    const schema = idoraId({ prefix: 'usr' }).nullable().optional();

    // Act & Assert
    assert.equal(schema.parse(undefined), undefined);
    assert.equal(schema.parse(null), null);
    assert.equal(await schema.parseAsync(USER_ID), USER_ID);
  });

  it('describes itself as a string in JSON Schema', () => {
    // Act
    const jsonSchema = z.toJSONSchema(z.object({ id: idoraId({ prefix: 'usr' }) }));

    // Assert
    assert.deepEqual((jsonSchema.properties as Record<string, unknown>)['id'], { type: 'string' });
  });
});

describe('idoraId({ prefix })', () => {
  const schema = idoraId({ prefix: 'usr' });

  it('accepts an identifier with exactly that prefix', () => {
    // Act & Assert
    assert.equal(schema.parse(USER_ID), USER_ID);
  });

  it('rejects another prefix, naming both', () => {
    // Act
    const issue = onlyIssue(schema.safeParse(ORDER_ID));

    // Assert
    assert.equal(issue.message, "Invalid identifier: expected the prefix 'usr', received 'ord'");
    assert.deepEqual(paramsOf(issue), { reason: 'prefixMismatch', expectedPrefix: 'usr' });
  });

  it('compares the prefix exactly, case included', () => {
    // Act & Assert
    assert.equal(paramsOf(onlyIssue(schema.safeParse(USER_ID.replace('usr', 'USR')))).reason, 'prefixMismatch');
  });

  it('uses the expected prefix in the example when the separator is missing', () => {
    // Act & Assert
    assert.match(onlyIssue(schema.safeParse('nope')).message, /such as usr_01k2f9v8ta7m3x0qbrn4dzeh5c$/);
  });

  it('refuses an invalid prefix when the schema is built', () => {
    // Act & Assert
    assert.throws(() => idoraId({ prefix: 'usr_' }), InvalidEntityPrefixError);
    assert.throws(() => idoraId({ prefix: 42 as never }), TypeError);
  });
});

describe('idoraId({ entity, registry })', () => {
  it('accepts identifiers with the prefix the entity type was registered with', () => {
    // Arrange
    const fromGenerator = idoraId({ entity: Order, registry: ids });
    const fromRegistry = idoraId({ entity: Order, registry: ids.registry });

    // Act & Assert
    assert.equal(fromGenerator.parse(ORDER_ID), ORDER_ID);
    assert.equal(fromRegistry.parse(ORDER_ID), ORDER_ID);
    assert.deepEqual(paramsOf(onlyIssue(fromGenerator.safeParse(USER_ID))), { reason: 'prefixMismatch', expectedPrefix: 'ord' });
  });

  it('throws when the schema is built for an unregistered entity type', () => {
    // Act & Assert
    assert.throws(() => idoraId({ entity: Unregistered, registry: ids }), EntityPrefixNotRegisteredError);
  });

  it('refuses contradictory or incomplete options', () => {
    // Act & Assert
    assert.throws(() => idoraId({ entity: User } as never), /no registry/);
    assert.throws(() => idoraId({ registry: ids } as never), /no entity/);
    assert.throws(() => idoraId({ entity: User, registry: ids, prefix: 'usr' } as never), /both a prefix and an entity/);
    assert.throws(() => idoraId({ entity: User, registry: {} as never }), /no registry/);
    assert.throws(() => idoraId(null as never), TypeError);
  });
});

describe('the error option', () => {
  it('replaces the message with a fixed string', () => {
    // Arrange
    const schema = idoraId({ prefix: 'usr', error: 'Not a user' });

    // Act & Assert
    assert.equal(onlyIssue(schema.safeParse(ORDER_ID)).message, 'Not a user');
  });

  it('builds the message from the InvalidIdError, keeping the params', () => {
    // Arrange
    const schema = idoraId({ entity: User, registry: ids, error: (error) => `${error.reason}: ${error.text}` });

    // Act
    const issue = onlyIssue(schema.safeParse(ORDER_ID));

    // Assert
    assert.equal(issue.message, `prefixMismatch: ${ORDER_ID}`);
    assert.deepEqual(paramsOf(issue), { reason: 'prefixMismatch', expectedPrefix: 'usr' });
  });
});

describe('parsedIdoraId()', () => {
  it('decodes into an IdoraId', () => {
    // Arrange
    const schema = z.object({ after: parsedIdoraId({ prefix: 'usr' }) });

    // Act
    const { after } = schema.parse({ after: USER_ID });

    // Assert
    assert.ok(after instanceof IdoraId);
    const prefix: 'usr' = after.prefix;
    assert.equal(prefix, 'usr');
    assert.equal(after.createdAt.toISOString(), '2025-08-07T12:00:00.000Z');
  });

  it('rejects what idoraId rejects, with the same issue', () => {
    // Act
    const issue = onlyIssue(parsedIdoraId({ entity: User, registry: ids }).safeParse(ORDER_ID));

    // Assert
    assert.deepEqual(paramsOf(issue), { reason: 'prefixMismatch', expectedPrefix: 'usr' });
  });

  it('encodes an IdoraId back into its text', () => {
    // Arrange
    const schema = z.object({ id: parsedIdoraId() });

    // Act
    const encoded = z.encode(schema, { id: IdoraId.parse(ORDER_ID) });

    // Assert
    assert.deepEqual(encoded, { id: ORDER_ID });
  });

  it('refuses to encode an IdoraId with the wrong prefix, or anything that is not an IdoraId', () => {
    // Arrange
    const schema = parsedIdoraId({ prefix: 'usr' });

    // Act & Assert
    assert.deepEqual(paramsOf(onlyIssue(z.safeEncode(schema, IdoraId.parse(ORDER_ID) as IdoraId<'usr'>))).reason, 'prefixMismatch');
    assert.match(onlyIssue(z.safeEncode(schema, USER_ID as never)).message, /expected an IdoraId/);
  });

  it('describes its input as a string in JSON Schema', () => {
    // Act
    const jsonSchema = z.toJSONSchema(parsedIdoraId(), { io: 'input' });

    // Assert
    assert.equal(jsonSchema.type, 'string');
  });
});
