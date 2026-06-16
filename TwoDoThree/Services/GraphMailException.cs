namespace TwoDoThree.Services;

public sealed class GraphMailException : Exception
{
    public GraphMailException(string message)
        : base(message)
    {
    }
}
