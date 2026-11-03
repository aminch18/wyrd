namespace Telecom;

public enum RuleError
{
    InvalidInput,
    AlreadyExists,
    WrongOwner,
    StaleAttempt,
    IllegalTransition,
    Irreversible,
    NotReady,
    ConflictingReceipt,
}
