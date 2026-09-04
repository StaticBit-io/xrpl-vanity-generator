using System;

namespace XrplVanity.Gpu.OpenCl;

public sealed class OpenClException : Exception
{
    public OpenClException(int code, string operation, string message)
        : base($"{operation} failed with OpenCL error {code} ({OpenClNative.ErrorName(code)}): {message}")
    {
        Code = code;
    }

    public int Code { get; }
}
