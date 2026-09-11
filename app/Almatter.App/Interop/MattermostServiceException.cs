using System;

namespace Almatter.App.Interop;

public sealed class MattermostServiceException : Exception
{
    public MattermostServiceException(string message) : base(message)
    {
    }
}
