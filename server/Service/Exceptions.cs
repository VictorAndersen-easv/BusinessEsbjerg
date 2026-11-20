namespace Service;

/// <summary>Thrown when a request breaks a business rule. The API turns it into a 400 with this message.</summary>
public class RuleViolationException(string message) : Exception(message);

/// <summary>Thrown when something the caller asked for does not exist. The API turns it into a 404.</summary>
public class NotFoundException(string message) : Exception(message);