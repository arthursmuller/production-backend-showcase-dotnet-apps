using System;
using System.Security.Cryptography;
using System.Text;

namespace Comment.Application.Features.Interactions;

public static class CommentInteractionMessageIdentity
{
    public static Guid Create(params object[] components)
    {
        var value = string.Join("|", components ?? Array.Empty<object>());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        guid[6] = (byte)((guid[6] & 0x0f) | 0x50);
        guid[8] = (byte)((guid[8] & 0x3f) | 0x80);
        return new Guid(guid);
    }
}
