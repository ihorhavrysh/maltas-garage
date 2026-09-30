namespace MaltasGarage.Domain.Exceptions;

/// <summary>
/// Thrown when something tries to buy, bid on, offer on, edit or delete a permanent
/// showcase listing of the public demo.
/// </summary>
public class ShowcaseListingException : InvalidOperationException
{
    public const string DefaultMessage =
        "This is a permanent showcase listing and cannot be changed in the demo. " +
        "Browse the other listings or create your own to try this feature.";

    public ShowcaseListingException() : base(DefaultMessage)
    {
    }
}
