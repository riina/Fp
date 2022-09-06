using System;
using System.Diagnostics.CodeAnalysis;
#if NET6_0_OR_GREATER
using System.Numerics;
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace Fp;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public partial class Processor
{
    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="value">AND value.</param>
    public static void ApplyOr(Span<byte> span, byte value)
    {
#if NET6_0_OR_GREATER
        if (Avx2.IsSupported)
            ApplyOrAvx2(span, value);
        else if (Sse2.IsSupported)
            ApplyOrSse2(span, value);
        else if (AdvSimd.IsSupported)
            ApplyOrAdvSimd(span, value);
        else if (Vector.IsHardwareAccelerated && span.Length >= Vector<byte>.Count)
            ApplyOrVectorized(span, value);
        else
            ApplyOrFallback(span, value);
#else
        ApplyOrFallback(span, value);
#endif
    }

    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="pattern">AND value.</param>
    /// <param name="sequenceBehaviour">Key behaviour.</param>
    public static void ApplyOr(Span<byte> span, ReadOnlySpan<byte> pattern, SequenceBehaviour sequenceBehaviour)
    {
#if NET6_0_OR_GREATER
        if (Vector.IsHardwareAccelerated && span.Length >= Vector<byte>.Count && pattern.Length >= Vector<byte>.Count)
            ApplyOrVectorized(span, pattern, sequenceBehaviour);
        else
            ApplyOrFallback(span, pattern, sequenceBehaviour);
#else
        ApplyOrFallback(span, pattern, sequenceBehaviour);
#endif
    }

#if NET6_0_OR_GREATER

    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="value">OR value.</param>
    public static unsafe void ApplyOrAdvSimd(Span<byte> span, byte value)
    {
        if (!AdvSimd.IsSupported) throw new PlatformNotSupportedException();
        const int split = 128 / 8;
        fixed (byte* pSource = span)
        {
            int i = 0;
            int l = span.Length;

            int kill1Idx = Math.Min((int)unchecked((ulong)(split - (long)pSource) % split), l);
            while (i < kill1Idx)
            {
                pSource[i] |= value;
                i++;
            }

            if (kill1Idx == l) return;

            var src = FillVector128AdvSimd(value);
            int kill2Idx = l - l % split;
            while (i < kill2Idx)
            {
                AdvSimd.Store(pSource + i, AdvSimd.Or(AdvSimd.LoadVector128(pSource + i), src));
                i += split;
            }

            while (i < span.Length)
            {
                pSource[i] |= value;
                i++;
            }
        }
    }

    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="value">OR value.</param>
    public static unsafe void ApplyOrSse2(Span<byte> span, byte value)
    {
        if (!Sse2.IsSupported) throw new PlatformNotSupportedException();
        const int split = 128 / 8;
        fixed (byte* pSource = span)
        {
            int i = 0;
            int l = span.Length;

            int kill1Idx = Math.Min((int)unchecked((ulong)(split - (long)pSource) % split), l);
            while (i < kill1Idx)
            {
                pSource[i] |= value;
                i++;
            }

            if (kill1Idx == l) return;

            var src = FillVector128Sse2(value);
            int kill2Idx = l - l % split;
            while (i < kill2Idx)
            {
                Sse2.StoreAligned(pSource + i, Sse2.Or(Sse2.LoadAlignedVector128(pSource + i), src));
                i += split;
            }

            while (i < span.Length)
            {
                pSource[i] |= value;
                i++;
            }
        }
    }

    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="value">OR value.</param>
    public static unsafe void ApplyOrAvx2(Span<byte> span, byte value)
    {
        if (!Avx2.IsSupported) throw new PlatformNotSupportedException();
        const int split = 256 / 8;
        fixed (byte* pSource = span)
        {
            int i = 0;
            int l = span.Length;

            int kill1Idx = Math.Min((int)unchecked((ulong)(split - (long)pSource) % split), l);
            while (i < kill1Idx)
            {
                pSource[i] |= value;
                i++;
            }

            if (kill1Idx == l) return;

            var src = FillVector256Avx(value);
            int kill2Idx = l - l % split;
            while (i < kill2Idx)
            {
                Avx.StoreAligned(pSource + i, Avx2.Or(Avx.LoadAlignedVector256(pSource + i), src));
                i += split;
            }

            while (i < span.Length)
            {
                pSource[i] |= value;
                i++;
            }
        }
    }

    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="value">OR value.</param>
    public static void ApplyOrVectorized(Span<byte> span, byte value)
    {
        if (!Vector.IsHardwareAccelerated) throw new PlatformNotSupportedException();
        int index = 0;
        Vector<byte> pattern = new(value);
        while (index + Vector<byte>.Count <= span.Length)
        {
            Vector.BitwiseOr(new Vector<byte>(span.Slice(index)), pattern).CopyTo(span.Slice(index));
            index += Vector<byte>.Count;
        }
        for (int i = index; i < span.Length; i++)
            span[i] |= value;
    }

    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="pattern">OR value.</param>
    /// <param name="sequenceBehaviour">Key behaviour.</param>
    public static void ApplyOrVectorized(Span<byte> span, ReadOnlySpan<byte> pattern, SequenceBehaviour sequenceBehaviour)
    {
        if (!Vector.IsHardwareAccelerated) throw new PlatformNotSupportedException();
        if (span.IsEmpty || pattern.IsEmpty) return;
        switch (sequenceBehaviour)
        {
            case SequenceBehaviour.Truncate:
                {
                    if (pattern.Length < span.Length)
                    {
                        int index = 0;
                        while (index + Vector<byte>.Count <= pattern.Length)
                        {
                            Span<byte> targetMemory = span[index..];
                            Vector<byte> sourceVec = new(targetMemory);
                            sourceVec = Vector.BitwiseOr(sourceVec, new Vector<byte>(pattern[index..]));
                            sourceVec.CopyTo(targetMemory);
                            index += Vector<byte>.Count;
                        }
                        for (int i = index; i < pattern.Length; i++)
                            span[i] |= pattern[i];
                    }
                    else
                    {
                        int index = 0;
                        while (index + Vector<byte>.Count <= span.Length)
                        {
                            Span<byte> targetMemory = span[index..];
                            Vector<byte> sourceVec = new(targetMemory);
                            sourceVec = Vector.BitwiseOr(sourceVec, new Vector<byte>(pattern[index..]));
                            sourceVec.CopyTo(targetMemory);
                            index += Vector<byte>.Count;
                        }
                        for (int i = index; i < span.Length; i++)
                            span[i] |= pattern[i];
                    }
                    break;
                }
            case SequenceBehaviour.Repeat:
                {
                    Span<byte> segment = span;
                    while (true)
                    {
                        if (pattern.Length < segment.Length)
                        {
                            int index = 0;
                            while (index + Vector<byte>.Count <= pattern.Length)
                            {
                                Span<byte> targetMemory = segment[index..];
                                Vector<byte> sourceVec = new(targetMemory);
                                sourceVec = Vector.BitwiseOr(sourceVec, new Vector<byte>(pattern[index..]));
                                sourceVec.CopyTo(targetMemory);
                                index += Vector<byte>.Count;
                            }
                            for (int i = index; i < pattern.Length; i++)
                                segment[i] |= pattern[i];
                            segment = segment[pattern.Length..];
                        }
                        else
                        {
                            int index = 0;
                            while (index + Vector<byte>.Count <= segment.Length)
                            {
                                Span<byte> targetMemory = segment[index..];
                                Vector<byte> sourceVec = new(targetMemory);
                                sourceVec = Vector.BitwiseOr(sourceVec, new Vector<byte>(pattern[index..]));
                                sourceVec.CopyTo(targetMemory);
                                index += Vector<byte>.Count;
                            }
                            for (int i = index; i < segment.Length; i++)
                                segment[i] |= pattern[i];
                            break;
                        }
                    }
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(sequenceBehaviour), sequenceBehaviour, null);
        }
    }

#endif

    /// <summary>
    /// Applies OR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="value">OR value.</param>
    public static void ApplyOrFallback(Span<byte> span, byte value)
    {
        for (int i = 0; i < span.Length; i++) span[i] |= value;
    }

    /// <summary>
    /// Applies XOR to memory.
    /// </summary>
    /// <param name="span">Memory to modify.</param>
    /// <param name="pattern">XOR value.</param>
    /// <param name="sequenceBehaviour">Key behaviour.</param>
    public static void ApplyOrFallback(Span<byte> span, ReadOnlySpan<byte> pattern, SequenceBehaviour sequenceBehaviour)
    {
        if (span.IsEmpty || pattern.IsEmpty) return;
        switch (sequenceBehaviour)
        {
            case SequenceBehaviour.Truncate:
                {
                    if (pattern.Length < span.Length)
                        for (int i = 0; i < pattern.Length; i++)
                            span[i] |= pattern[i];
                    else
                        for (int i = 0; i < span.Length; i++)
                            span[i] |= pattern[i];
                    break;
                }
            case SequenceBehaviour.Repeat:
                {
                    Span<byte> segment = span;
                    while (true)
                    {
                        if (pattern.Length < segment.Length)
                        {
                            for (int i = 0; i < pattern.Length; i++)
                                segment[i] |= pattern[i];
                            segment = segment[pattern.Length..];
                        }
                        else
                        {
                            for (int i = 0; i < segment.Length; i++)
                                segment[i] |= pattern[i];
                            break;
                        }
                    }
                    break;
                }
            default:
                throw new ArgumentOutOfRangeException(nameof(sequenceBehaviour), sequenceBehaviour, null);
        }
    }
}
