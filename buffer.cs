using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Runtime.InteropServices;
using static UtilStatic;
using UtilNs;
using System.Collections;
using FileAccess = System.IO.FileAccess;
using Stream = System.IO.Stream;
using UnmanagedMemoryStream = System.IO.UnmanagedMemoryStream;




namespace BufferNs
{


	/// <summary>
	/// represents a contiguous block of memory 
	/// </summary>
	public interface IBuffer : IDisposable
	{
		IntPtr Lock();
		void Unlock();

		/// <summary>
		/// Number of bytes in the memory block. This is required to not change across the lifetime of the object.
		/// </summary>
		long sizeinbytes { get; }
		IBuffer GetSlice(long offset, long size);

	}


	public static class IBufferExtensions
	{
		public static bool CheckRangeValid(this IBuffer buffer, long offsetinbytes, long sizeinbytes)
		{
			return (
				offsetinbytes >= 0 &&
				offsetinbytes <= buffer.sizeinbytes &&
				sizeinbytes >= 0 &&
				sizeinbytes <= buffer.sizeinbytes - offsetinbytes
				);

		}

		/// <summary>
		/// checks that the requested range is valid, prevents the memory from being moved by the gc, and returns a pointer to the element at the requested offset.
		/// </summary>
		public static IntPtr Lock(this IBuffer buffer, long offsetinbytes, long sizeinbytes)
		{
			BAssert(buffer.CheckRangeValid(offsetinbytes, sizeinbytes));

			//do it this way to clearly work with address ranges that cross the 2g 32bit boundary
			//note, CheckRangeValid screens out negative offsets
			ulong addr = (ulong) buffer.Lock() + (ulong) offsetinbytes;

			return checked((IntPtr) addr);
		}

		public static IntPtr Lock(this IBuffer buffer, long offsetinelements, long sizeinelements, Type elementtype)
		{
			return buffer.Lock(offsetinelements, sizeinelements, Marshal.SizeOf(elementtype));
		}
		public static IntPtr Lock(this IBuffer buffer, long offsetinelements, long sizeinelements, long elementsize)
		{
			checked
			{
				return buffer.Lock(offsetinelements * elementsize, sizeinelements * elementsize);
			}
		}

		public static unsafe void ZeroFill(this IBuffer buffer)
		{
			BAssert(sizeof(long) == 8);

			byte* start = (byte*) buffer.Lock();
			byte* end = start + buffer.sizeinbytes;

			while ((long) start % 8 != 0 && start < end)
			{
				*start = 0;
				start++;
			}

			while ((long) end % 8 != 0 && start < end)
			{
				end--;
				*end = 0;
			}

			for (long* cur_long = (long*) start; cur_long < (long*) end; cur_long++)
			{
				*cur_long = 0;
			}


			buffer.Unlock();


		}


		public static unsafe void CopyTo(this IBuffer src, IBuffer dest)
		{

			BAssert(src != null && dest != null && src.sizeinbytes == dest.sizeinbytes);
			void* src_ptr = null;
			void* dest_ptr = null;

			try
			{
				long bytestocopy = src.sizeinbytes;
				src_ptr = (void*) src.Lock(0, bytestocopy);
				dest_ptr = (void*) dest.Lock(0, bytestocopy);
				Buffer.MemoryCopy(src_ptr, dest_ptr, bytestocopy, bytestocopy); //requires .net 4.6
			}
			finally
			{
				if (src_ptr != null) { src.Unlock(); }
				if (dest_ptr != null) { dest.Unlock(); }
			}
		}


		public static void CopyTo(this IBuffer src, Array dest)
		{
			IBuffer buffer = new ArrayBackedBuffer(dest);
			src.CopyTo(buffer);
		}


		public static void CopyTo(this Array src, IBuffer dest)
		{
			IBuffer buffer = new ArrayBackedBuffer(src);
			buffer.CopyTo(dest);
		}


		public static IEnumerator<byte> GetEnumerator(this IBuffer buffer)
		{
			return new BufferByteEnumerator(buffer);
		}


		public static IEnumerable<byte> AsEnumerable(this IBuffer buffer)
		{
			return new BufferAsByteEnumerable(buffer);
		}

		public static Stream GetStream(this IBuffer buffer)
		{
			return new BufferStream(buffer);
		}



		/// <param name="numbytes">if null, will use the entire size of the IBuffer</param>
		public static void CopyTo(this Stream src, IBuffer dest, long? numbytes = null, long buffersize = 4096)
		{
			if (numbytes == null) { numbytes = dest.sizeinbytes; }
			using (var deststream = dest.GetStream())
			{
				src.CopyToPartial(deststream, (long) numbytes, buffersize);
			}
		}

		/// <param name="numbytes">if null, will use the entire size of the IBuffer</param>
		public static void CopyTo(this IBuffer src, Stream dest, long? numbytes = null, long buffersize = 4096)
		{
			if (numbytes == null) { numbytes = src.sizeinbytes; }
			using (var srcstream = src.GetStream())
			{
				srcstream.CopyToPartial(dest, (long) numbytes, buffersize);
			}
		}


	}




	/// <summary>
	/// describes a page (4k) aligned block of memory allocated by the win32 VirtualAlloc() function.
	/// </summary>
	public class VirtualAllocBuffer : IDisposable, IBuffer
	{

		public VirtualAllocBuffer(long size)
		{
			this.sizeinbytes = size;

			//using 2m pages
			//https://msdn.microsoft.com/en-us/library/windows/desktop/aa366720%28v=vs.85%29.aspx
			//uint pagesize = use_2m_pages ? MEM_LARGE_PAGES : 0;

			addr = VirtualAlloc(IntPtr.Zero, checked((IntPtr) size), MEM_RESERVE | MEM_COMMIT, 0);
			if (addr == IntPtr.Zero)
			{
				throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
			}

			GC.AddMemoryPressure(this.sizeinbytes);
		}

		object syncroot = new object();
		int lockrefcount = 0;
		IntPtr addr = IntPtr.Zero;
		public long sizeinbytes { get; private set; }

		public IntPtr Lock()
		{
			lock (syncroot)
			{
				BAssert(addr != IntPtr.Zero);
				lockrefcount += 1;
				return addr;
			}
		}

		public void Unlock()
		{
			lock (syncroot)
			{
				BAssert(lockrefcount > 0);
				BAssert(addr != IntPtr.Zero);
				lockrefcount -= 1;
			}
		}


		protected virtual void Dispose(bool disposing)
		{
			lock (syncroot)
			{
				BAssert(lockrefcount == 0);
				if (addr == IntPtr.Zero) { return; }
				bool returncode = VirtualFree(addr, IntPtr.Zero, MEM_RELEASE);
				if (!returncode)
				{
					throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
				}
				GC.RemoveMemoryPressure(this.sizeinbytes);
				addr = IntPtr.Zero;
			}
		}


		~VirtualAllocBuffer()
		{
			Dispose(false);
		}

		/// <summary>
		/// frees the memory managed by the block
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}


		public IBuffer GetSlice(long offsetinbytes, long sizeinbytes)
		{
			BAssert(this.CheckRangeValid(offsetinbytes, sizeinbytes));
			return new BufferSlice(this, offsetinbytes, sizeinbytes);
		}


		[DllImport("Kernel32.dll", SetLastError = true)]
		static extern IntPtr VirtualAlloc(IntPtr address, IntPtr size, uint allocationtype, uint protect);

#pragma warning disable CS0414 //value assigned but never used
		static readonly uint
			MEM_COMMIT = 0x1000,
			MEM_RESERVE = 0x2000,
			MEM_RESET = 0x80000,
			MEM_RESET_UNDO = 0x1000000,
			MEM_LARGE_PAGES = 0x20000000,
			MEM_PHYSICAL = 0x00400000,
			MEM_TOP_DOWN = 0x00100000,
			MEM_WRITE_WATCH = 0x00200000;

#pragma warning restore CS0414

		[DllImport("Kernel32.dll", SetLastError = true)]
		static extern bool VirtualFree(IntPtr address, IntPtr size, uint freetype);

#pragma warning disable CS0414 //value assigned but never used

		static readonly uint
			MEM_DECOMMIT = 0x4000,
			MEM_RELEASE = 0x8000; //also decommits; don't use with MEM_DECOMMIT

#pragma warning restore CS0414


	}



	public class ArrayBackedBuffer : IBuffer
	{
		Array buffer;
		GCHandle gchandle;
		public long sizeinbytes { get; private set; }
		object syncroot = new object();
		int lockrefcount = 0;
		public long elementsize { get; private set; }

		public ArrayBackedBuffer(Array buffer)
		{
			BAssert(buffer != null);
			BAssert(buffer.GetType().GetElementType().IsValueType, "not a value type array");
			BAssert(buffer.IsFixedSize, "not fixed size");
			BAssert(buffer.Rank == 1, "not implemented: multi dimensional array");
			BAssert(buffer.GetLowerBound(0) == 0);

			elementsize = Marshal.SizeOf(buffer.GetType().GetElementType());

			this.buffer = buffer;
			sizeinbytes = buffer.LongLength * elementsize;
		}

		public ArrayBackedBuffer(long size)
		{
			this.buffer = new byte[size];
			this.sizeinbytes = size;
			this.elementsize = 1;
		}


		public IntPtr Lock()
		{
			lock (syncroot)
			{
				BAssert(buffer != null);

				if (!gchandle.IsAllocated)
				{
					gchandle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
				}
				lockrefcount += 1;
				return gchandle.AddrOfPinnedObject();
			}
		}

		public void Unlock()
		{
			lock (syncroot)
			{
				BAssert(lockrefcount > 0);
				lockrefcount -= 1;

				BAssert(gchandle.IsAllocated);
				if (lockrefcount == 0)
				{
					gchandle.Free();
				}

			}
		}

		protected virtual void Dispose(bool disposing)
		{
			lock (syncroot)
			{
				//don't free the handle; just crash if something's still using it
				BAssert(!gchandle.IsAllocated, "attempt to dispose a still locked buffer");
				BAssert(lockrefcount == 0, "attempt to dispose a still locked buffer. also, handle somehow freed without decrementing refcount");

				//because this class wraps a managed array, there's nothing to actually deallocate
			}
		}

		~ArrayBackedBuffer()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
		}

		public IBuffer GetSlice(long offsetinbytes, long sizeinbytes)
		{
			BAssert(this.CheckRangeValid(offsetinbytes, sizeinbytes));
			return new BufferSlice(this, offsetinbytes, sizeinbytes);
		}
	}






	public class BufferSlice : IBuffer
	{
		IBuffer buffer;
		/// <summary>
		/// number of bytes contained in this slice
		/// </summary>
		public long sizeinbytes { get; private set; }
		/// <summary>
		/// offset into the buffer where the slice starts
		/// </summary>
		long offsetinbytes;




		public IntPtr Lock()
		{

			//do it this way to clearly work with address ranges that cross the 2g 32bit boundary
			//note, CheckRangeValid in the constructor screened out negative offsets
			ulong addr = (ulong) buffer.Lock() + (ulong) this.offsetinbytes;

			return checked((IntPtr) addr);
		}


		public void Unlock()
		{
			buffer.Unlock();
		}


		public BufferSlice(IBuffer buffer, long offsetinbytes, long sizeinbytes)
		{
			BAssert(buffer != null); //?
			BAssert(buffer.CheckRangeValid(offsetinbytes, sizeinbytes));

			this.sizeinbytes = sizeinbytes;
			this.offsetinbytes = offsetinbytes;
			this.buffer = buffer;
		}

		public BufferSlice GetSlice(long offsetinbytes, long sizeinbytes)
		{
			BAssert(this.CheckRangeValid(offsetinbytes, sizeinbytes), "bounds check fail");

			return new BufferSlice(this.buffer, offsetinbytes + this.offsetinbytes, sizeinbytes);

		}

		/// <summary>
		/// does nothing, because we do not own the buffer referenced
		/// </summary>
		public void Dispose()
		{
		}


		IBuffer IBuffer.GetSlice(long offsetinbytes, long sizeinbytes)
		{
			return this.GetSlice(offsetinbytes, sizeinbytes);
		}
	}







	public class ZeroLengthBuffer : IBuffer
	{
		public long sizeinbytes
		{
			get
			{
				return 0;
			}
		}

		public void Dispose()
		{
		}

		public IBuffer GetSlice(long offset, long size)
		{
			BAssert(offset == 0 && size == 0,
				$"attempt to get non null slice from class {nameof(ZeroLengthBuffer)}");
			return this;
		}

		public IntPtr Lock()
		{
			BAssert(false,
				$"attempt to lock value of class {nameof(ZeroLengthBuffer)}");
			throw new UnreachableException();
		}

		public void Unlock()
		{
			BAssert(false,
				$"attempt to unlock value of class {nameof(ZeroLengthBuffer)}");
			throw new UnreachableException();

		}
	}






	/// <summary>
	/// implements IEnumerable<byte> for IBuffer
	/// </summary>
	public class BufferAsByteEnumerable : IEnumerable<byte>
	{
		IBuffer buffer;
		public BufferAsByteEnumerable(IBuffer buffer) { this.buffer = buffer; }
		public IEnumerator<byte> GetEnumerator()
		{
			return new BufferByteEnumerator(buffer);
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}









	//"yield return" - style enumerators don't support unsafe code
	/// <summary>
	/// implements IEnumerator<byte> for IBuffer
	/// </summary>
	public unsafe class BufferByteEnumerator : IEnumerator<byte>
	{
		byte* cur;
		byte* end;
		IBuffer buffer;
		bool started = false;

		public BufferByteEnumerator(IBuffer buffer)
		{
			this.buffer = buffer;
			long bytes = buffer.sizeinbytes;
			cur = (byte*) buffer.Lock(0, bytes);
			end = cur + bytes;
		}


		public byte Current
		{
			get
			{
				if (disposedValue) { throw new ObjectDisposedException(nameof(BufferByteEnumerator)); }
				return *cur;
			}
		}

		object IEnumerator.Current
		{
			get { return (object) Current; }
		}

		public bool MoveNext()
		{
			if (!started)
			{
				started = true;
			}
			else if (cur < end)
			{
				cur++;
			}

			if (cur >= end)
			{
				Dispose();
				return false;
			}
			return true;

		}

		public void Reset()
		{
			throw new NotImplementedException();
		}


		#region IDisposable Support
		private bool disposedValue = false;

		protected virtual void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				cur = null;
				end = null;
				buffer.Unlock();

				disposedValue = true;
			}
		}

		~BufferByteEnumerator()
		{
			Dispose(false);
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		#endregion

	}




	public unsafe class BufferStream : UnmanagedMemoryStream
	{
		IBuffer buffer;


		public BufferStream(IBuffer buffer, FileAccess access = FileAccess.ReadWrite)
			: base((byte*) buffer.Lock(), buffer.sizeinbytes, buffer.sizeinbytes, access)
		{
			this.buffer = buffer;
		}

		bool disposedValue = false;

		protected override void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				base.Dispose(disposing);
				buffer.Unlock();
				disposedValue = true;
			}
		}

		~BufferStream() { Dispose(false); }
	}





}


