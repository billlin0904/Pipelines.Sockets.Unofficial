﻿using System;
using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Pipelines.Sockets.Unofficial.Arenas
{
    public abstract class Allocator<T>
    {
        static readonly int _defaultBlockSize = CalculateDefaultBlockSize();
        static int CalculateDefaultBlockSize()
        {
            try
            {
                // try for 64k *memory* (not 64k elements)
                int count = (64 * 1024) / UnsafeSize<T>();
                return count <= 64 ? 64 : count; // avoid too small (only impacts huge types)
            }
            catch
            {
                return 16 * 1024; // arbitrary 16k elements if that fails
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)] // because of assembly binding pain in Unsaef
        private static int UnsafeSize<T>() => Unsafe.SizeOf<T>();

        public virtual int DefaultBlockSize => _defaultBlockSize;

        public abstract IMemoryOwner<T> Allocate(int length);

        public virtual void Clear(IMemoryOwner<T> allocation, int length)
            => allocation.Memory.Span.Slice(0, length).Clear();
    }
    public sealed class ArrayPoolAllocator<T> : Allocator<T>
    {
        private readonly ArrayPool<T> _pool;
        public static ArrayPoolAllocator<T> Shared { get; } = new ArrayPoolAllocator<T>();

        public ArrayPoolAllocator(ArrayPool<T> pool = null) => _pool = pool ?? ArrayPool<T>.Shared;

        public override IMemoryOwner<T> Allocate(int length)
            => new OwnedArray(_pool, _pool.Rent(length));

        sealed class OwnedArray : IMemoryOwner<T>
        {
            private T[] _array;
            private readonly ArrayPool<T> _pool;
            public OwnedArray(ArrayPool<T> pool, T[] array)
            {
                _pool = pool;
                _array = array;
            }

            public Memory<T> Memory => _array;

            public void Dispose()
            {
                var arr = _array;
                _array = null;
                if (arr != null) _pool.Return(arr);
            }
        }
    }

    public unsafe sealed class UnmanagedAllocator<T> : Allocator<T> where T : unmanaged
    {
        private UnmanagedAllocator() { }
        public static UnmanagedAllocator<T> Shared { get; } = new UnmanagedAllocator<T>();

        public override IMemoryOwner<T> Allocate(int length) => new OwnedPointer(length);

        sealed class OwnedPointer : MemoryManager<T>
        {
            ~OwnedPointer() => Dispose(false);

            private T* _ptr;
            private readonly int _length;

            public OwnedPointer(int length)
                => _ptr = (T*)Marshal.AllocHGlobal((_length = length) * sizeof(T)).ToPointer();

            public override Span<T> GetSpan() => new Span<T>(_ptr, _length);

            public override MemoryHandle Pin(int elementIndex = 0)
                => new MemoryHandle(_ptr + elementIndex);
            public override void Unpin() { } // nothing to do

            protected override void Dispose(bool disposing)
            {
                var ptr = _ptr;
                _ptr = null;
                if (ptr != null) Marshal.FreeHGlobal(new IntPtr(ptr));
                if (disposing) GC.SuppressFinalize(this);
            }
        }
    }
}
