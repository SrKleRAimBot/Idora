/** Plain entity classes, registered explicitly by the tests that need them. */
export class User {}
export class Order {}
export class Invoice {}
export class Account {}
export class Accrual {}
export abstract class AbstractEntity {}

/** Used to prove that an unregistered class is rejected rather than guessed at. */
export class UnregisteredEntity {}
