
using System;
using System.Runtime.InteropServices;
using static UtilStatic;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Collections;
using UtilNs;







public static class UtilStatic
{
	public static bool BAssert(bool b, string msg = "")
	{
		if(msg != null && msg != "")
		{
			msg = ": " + msg;
		}

		if (!b)
		{
			throw new Exception($"Assert failed{msg}");
		}

		return true;
	}

	public static void CopyToPartial(this Stream src, Stream dest, long numbytes, long buffersize = 4096)
	{
		if (numbytes < 0)
			{ throw new ArgumentOutOfRangeException(nameof(numbytes)); }
		if (buffersize > int.MaxValue)
			{ throw new ArgumentOutOfRangeException(nameof(buffersize), "class Stream only supports int sized reads"); }

		
		var buffer = new byte[buffersize];

		while (numbytes > 0)
		{
			long bytestoget = Math.Min(buffersize, numbytes);
			long actuallygotten = src.Read(buffer, 0, checked((int) buffersize));

			if (actuallygotten == 0) { break; } //Stream.Read returns 0 only when at end of stream

			dest.Write(buffer, 0, checked((int) actuallygotten));
			numbytes -= actuallygotten;
		}


		if(numbytes != 0) { throw new IOException("source stream not long enough"); }
	}


	public static byte[] ToUtf8Bytes(this string s)
	{
		return Encoding.UTF8.GetBytes(s);
	}

	public static bool IsNullOrEmpty(this string s)
	{
		return s == null || s.Length == 0;
	}

	public static Stream ZeroPad(this Stream s, long totallength)
	{
		return Util.ZeroPadStream(s, totallength);
	}


}


public static partial class Util
{
	
	public static int SizeOfArrayElement(Array a)
	{
		return Marshal.SizeOf(a.GetType().GetElementType());
	}

	public static long SizeOfArray(Array a)
	{
		return SizeOfArrayElement(a) * a.LongLength;
	}

	public static void ConsolePause(string prompt = "press enter to continue")
	{
		Console.Write(prompt);
		Console.ReadLine();
	}



	/// <summary>
	/// ignores all but the lowest 4 bits
	/// </summary>
	public static char HexNibbleToChar(int n)
	{
		n &= 0xf;
		return (n < 10) ? (char)(n + '0') : (char)(n - 10 + 'a');
	}


	/// <summary>
	/// 
	/// </summary>
	/// <param name="data"></param>
	/// <param name="byteseparator">added between bytes within a group, not added between groups</param>
	/// <param name="groupseparator"></param>
	/// <param name="bytespergroup"></param>
	/// <param name="memoryorderendian">true: bytes appear in same order as in memory. false: bytes appear as if printing an int.</param>
	/// <returns></returns>
	public static string FormatHex(IEnumerable<byte> data, string byteseparator = null, string groupseparator = " ", int bytespergroup = 1, bool memoryorderendian = true)
	{

		StringBuilder output = new StringBuilder();
		var groupbuffer = new byte[bytespergroup];
		int groupbufferbytesused = bytespergroup;
		bool done = false;
		bool firstgroup = true;

		using (var e = data.GetEnumerator())
		{

			while (!done)
			{
				Array.Clear(groupbuffer, 0, groupbuffer.Length);

				for (int i = 0; i < bytespergroup; i++)
				{
					if (e.MoveNext())
					{
						groupbuffer[i] = e.Current;
					}
					else
					{
						done = true;
						groupbufferbytesused = i;
						break;
					}
				}

				if (groupbufferbytesused == 0) { continue; }

				if (firstgroup) { firstgroup = false; }
				else { output.Append(groupseparator); }


				int begin, end, increment;
				if (memoryorderendian) { begin = 0; end = groupbufferbytesused; increment = 1; }
				else { begin = groupbufferbytesused - 1; end = -1; increment = -1; }


				for (int i = begin; i != end; i += increment)
				{
					byte b = groupbuffer[i];
					if (i != begin) { output.Append(byteseparator); }
					output.Append(HexNibbleToChar(b >> 4));
					output.Append(HexNibbleToChar(b >> 0));
				}

			}

		}

		return output.ToString();
	}


	/// <summary>
	/// Pads the end of a given stream with zeroes to a specified length. Reads beyond the given stream's end return zeroes. 
	/// Writes beyond the given stream's end are ignored. Neither the returned stream nor the given stream can be resized.
	/// </summary>
	public static Stream ZeroPadStream(Stream s, long totallength)
	{
		return new ConcatenatedStream(new[] { s, new ZeroFilledStream(totallength - s.Length) });
	}

	
	/// <summary>
	/// Creates an IList where every element is a transformation of a corresponding element of another IList.
	/// For example, could create an IList&lt;int&gt; from an IList of a class with an int field.
	/// Adding/removing elements from this list will fail; manipulate the source list instead.
	/// </summary>
	/// <typeparam name="TThis">type of the elements of the returned list</typeparam>
	/// <typeparam name="TSource">type of the elements of the source list</typeparam>
	/// <param name="sourcelist"></param>
	/// <param name="getterfunc"></param>
	/// <param name="setterfunc">leave null to not support setting</param>
	/// <returns></returns>
	public static IList<TThis> MapList<TThis, TSource>(IList<TSource> sourcelist, Func<TSource, TThis> getterfunc, Action<TSource, TThis> setterfunc = null)
	{
		if (setterfunc == null) { setterfunc = (obj, value) => { throw new NotSupportedException("setter function not provided"); }; }
		return new FunctionalList<TThis>(
			() => sourcelist.Count,
			(index) => getterfunc(sourcelist[index]),
			(index, value) => setterfunc(sourcelist[index], value)
			);
		
	}



}




namespace UtilNs
{

	public static class FileUtil
	{
		public static void Create(string path, long size)
		{
			using(var file = File.Create(path))
			{
				file.SetLength(size);
			}
		}

		public static long GetLength(string path)
		{
			return new FileInfo(path).Length;
		}


		/// <summary>
		/// opens existing file with all access
		/// </summary>
		public static Stream Open(string path, bool exclusive = false)
		{
			return File.Open(path, FileMode.Open, FileAccess.ReadWrite, exclusive ? FileShare.None : FileShare.ReadWrite);
		}

	}


	/// <summary>
	/// an IList backed by functions instead of data, where all access goes through the functions provided to the constructor.
	/// Supply a function for getting the size of the list, getting the nth item and (optionally) setting the nth item, 
	/// and things mostly work as you would expect.
	/// Does not support the IList methods that insert or remove items.
	/// IndexOf and Contains do a simple brute force search using EqualityComparer&lt;T&gt;.Default 
	/// </summary>
	/// <typeparam name="T"></typeparam>
	public class FunctionalList<T> : IList<T>
	{
		Func<int> countfunc;
		Func<int, T> getterfunc;
		Action<int, T> setterfunc;

		public T this[int index]
		{
			get
			{
				AssertValidIndex(index);
				return getterfunc(index);
			}

			set
			{
				AssertValidIndex(index);
				setterfunc(index, value);
			}
		}

		void AssertValidIndex(int index)
		{
			if(index < 0 || index >= Count)
				{ throw new IndexOutOfRangeException(); }
		}

		public int Count
		{
			get { return countfunc(); }
		}

		public bool IsReadOnly
		{
			get { return false; }
		}

		public FunctionalList(Func<int> countfunc, Func<int, T> getterfunc, Action<int, T> setterfunc = null)
		{
			if (setterfunc == null)
			{
				string s = nameof(FunctionalList<T>);
				setterfunc = (index, value) => { throw new NotSupportedException($"{s}: setter function not provided"); };
			}
			this.countfunc = countfunc;
			this.getterfunc = getterfunc;
			this.setterfunc = setterfunc;
		}

		public FunctionalList(int count, Func<int, T> getterfunc, Action<int, T> setterfunc = null)
			: this( () => count, getterfunc, setterfunc )
		{

		}

		public void CopyTo(T[] array, int arrayIndex)
		{
			for (int i = 0; i < this.Count; i++)
			{
				array[arrayIndex + i] = this[i];
			}
		}

		public IEnumerator<T> GetEnumerator()
		{
			for (int i = 0; i < this.Count; i++)
			{
				yield return this[i];
			}
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}

		public int IndexOf(T item)
		{
			var comparer = EqualityComparer<T>.Default;

			for (int i = 0; i < Count; i++)
			{
				if( comparer.Equals(this[i], item) )
					{ return i; }
			}
			return -1;
		}

		public bool Contains(T item)
		{
			return IndexOf(item) != -1;
		}
		

		static readonly string errormsg = $"{nameof(FunctionalList<T>)} class does not support inserting or removing items directly";

		public void Add(T item)
		{
			throw new NotSupportedException(errormsg);
		}

		public void Clear()
		{
			throw new NotSupportedException(errormsg);
		}

		public void Insert(int index, T item)
		{
			throw new NotSupportedException(errormsg);
		}

		public bool Remove(T item)
		{
			throw new NotSupportedException(errormsg);
		}

		public void RemoveAt(int index)
		{
			throw new NotSupportedException(errormsg);
		}
	}



	/// <summary>
	/// all reads return zeroes, all writes are ignored
	/// </summary>
	public class ZeroFilledStream : Stream
	{
		public ZeroFilledStream(long length)
		{
			_canread = true;
			_canwrite = true;
			_canseek = true;
			_position = 0;
			_length = length;
		}

		bool _canread, _canwrite, _canseek;
		long _length, _position = 0;


		public override bool CanRead { get { return _canread; } }

		public override bool CanSeek { get { return _canseek; } }

		public override bool CanWrite { get { return _canwrite; } }

		public override long Length { get { return _length; } }

		public override long Position
		{
			get { return _position; }

			set
			{
				//if (value < 0 || value > Length)
				//according to the docs, you should let the operation complete even if position is out of range

				_position = value;

			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (!CanRead) { throw new NotSupportedException(); }
			if (_position < 0 || _position >= _length) { return 0; }
			count = (int) Math.Min(count, Length - Position);
			Array.Clear(buffer, offset, count);
			Position = Position + count;
			return count;
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			if (!CanSeek) { throw new NotSupportedException(); }
			switch (origin)
			{
				case SeekOrigin.Begin:
					Position = offset;
					return Position;
				case SeekOrigin.Current:
					Position += offset;
					return Position;
				case SeekOrigin.End:
					Position = Length + offset;
					return Position;
				default:
					throw new Exception($"unknown or not implemented SeekOrigin value: {origin}");
			}
		}

		public override void SetLength(long value)
		{
			if (!CanSeek) { throw new NotSupportedException(); }
			_length = value;
			if (Position > _length) { Position = _length; }
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (!CanWrite) { throw new NotSupportedException(); }
			//ignore all writes
		}

		public override void Flush()
		{
			//nothing to do here
		}

	}



	class LongRange : IComparable<LongRange>
	{
		/// <summary>
		/// the first int contained in the range
		/// </summary>
		public long begin;
		/// <summary>
		/// one past the last int contained in the range
		/// </summary>
		public long end;
		public LongRange(long begin, long end)
		{
			if (end < begin) { throw new ArgumentException("end < begin"); }
			this.begin = begin;
			this.end = end;
		}

		public bool Contains(long value)
		{
			return value >= begin && value < end;
		}


		/// <summary>
		/// also returns 0 if one range is contained within the other
		/// </summary>
		public int CompareTo(LongRange other)
		{
			if (this.begin == other.begin)
			{
				//make sure a zero length (begin==end) range is reported as being contained within
				//the non-zero length range that that number would be within.
				//for example if r1.end == this.begin, this.begin == this.end, and this.end == r2.begin,
				//this should be said to be contained within r2 not r1,
				//since the number n=this.begin would fall within r2 and not r1.
				return 0;
			}

			if (this.begin < other.begin && this.end <= other.begin) { return -1; }
			if (other.begin < this.begin && other.end <= this.begin) { return 1; }
			if ((this.begin < other.begin && this.end >= other.end) ||
				(other.begin < this.begin && other.end >= this.end)) { return 0; }
			throw new Exception("ranges overlap");
		}

	}


	/// <summary>
	/// does not support being resized and assumes that the component streams will not be resized.
	/// </summary>
	public class ConcatenatedStream : Stream
	{

		IList<Stream> streams = new List<Stream>();

		/// <summary>
		/// only valid if canseek==true. 
		/// the "value" parameter is ignored; the index of each key (positionlookup.IndexOfKey) is the thing to be looked up
		/// </summary>
		SortedList<LongRange, int> positionlookup = new SortedList<LongRange, int>();
		int currentstreamindex = 0;

		public ConcatenatedStream(IEnumerable<Stream> streams)
		{
			_canread = true;
			_canwrite = true;
			_canseek = true;

			foreach (var stream in streams)
			{
				_canread &= stream.CanRead;
				_canwrite &= stream.CanWrite;
				_canseek &= stream.CanSeek;

				if (_canseek)
				{
					long newlength = checked(_length + stream.Length);
					positionlookup.Add(new LongRange(_length, newlength), 0);
					_length = newlength;

				}

				this.streams.Add(stream);
			}
		}


		/// <summary>
		/// returns the index of which component stream corresponds to the given position in this stream
		/// </summary>
		int GetStreamIndex(long position)
		{
			if (!CanSeek) { throw new NotSupportedException(); }
			if (position < 0 || position >= _length) { throw new ArgumentOutOfRangeException(); }
			return positionlookup.IndexOfKey(new LongRange(position, position));
		}


		bool _canread, _canwrite, _canseek;
		long _length = 0, _position = 0;

		/// <summary>
		/// for some reason, the docs don't want you to throw an error when seeking outside the valid bounds of the stream.
		/// so keep track here whether the position is within valid bounds.
		/// note, this will only be set to false when the user manually calls seek()/sets Position
		/// </summary>
		bool _positionisvalid = true;

		public override bool CanRead { get { return _canread; } }

		public override bool CanSeek { get { return _canseek; } }

		public override bool CanWrite { get { return _canwrite; } }

		public override long Length { get { return _length; } }

		public override long Position
		{
			get { return _position; }

			set
			{
				if (disposedValue) { throw new ObjectDisposedException(nameof(ConcatenatedStream)); }
				if (!CanSeek) { throw new NotSupportedException(); }


				_position = value;
				if (value < 0 || value >= Length)
				{ _positionisvalid = false; return; }
				else { _positionisvalid = true; }


				currentstreamindex = positionlookup.IndexOfKey(new LongRange(_position, _position));

				long positionwithincurrent = _position - positionlookup.Keys[currentstreamindex].begin;
				BAssert(positionwithincurrent >= 0 && positionwithincurrent < positionlookup.Keys[currentstreamindex].end);
				streams[currentstreamindex].Seek(positionwithincurrent, SeekOrigin.Begin);

			}
		}

		public override void Flush()
		{
			if (disposedValue) { throw new ObjectDisposedException(nameof(ConcatenatedStream)); }
			foreach (var stream in streams) { stream?.Flush(); }
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (disposedValue) { throw new ObjectDisposedException(nameof(ConcatenatedStream)); }
			int totalbytesactuallyread = 0;
			if (!CanRead) { throw new NotSupportedException(); }
			if (!_positionisvalid) { return 0; }

			while (count > 0 && currentstreamindex < streams.Count)
			{
				while (count >= 0) //read from single stream
				{
					int numbytesactuallyread = streams[currentstreamindex].Read(buffer, offset, count);
					BAssert(numbytesactuallyread <= count);
					checked
					{
						offset += numbytesactuallyread;
						count -= numbytesactuallyread;
						_position += numbytesactuallyread;
						totalbytesactuallyread += numbytesactuallyread;
					}
					if (numbytesactuallyread == 0) { break; } //0 is only returned upon end of stream
				}

				//increment stream index if necessary

				if (count == 0) { break; }

				if (CanSeek) //check the stream hasn't been resized
				{
					if (_position != positionlookup.Keys[currentstreamindex].end)
					{
						throw new Exception($"stream resized in the middle of use in {nameof(ConcatenatedStream)}");
					}
				}

				currentstreamindex += 1;
				if (CanSeek) { streams[currentstreamindex].Seek(0, SeekOrigin.Begin); }

			}

			return totalbytesactuallyread;

		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (disposedValue) { throw new ObjectDisposedException(nameof(ConcatenatedStream)); }
			if (!CanWrite) { throw new NotSupportedException(); }
			if (!CanSeek)
			{
				throw new NotSupportedException(
					$"writing to a {nameof(ConcatenatedStream)} requires knowing the end position of each individual stream, which requires CanSeek.");
			}
			if (Position + count > Length || !_positionisvalid) { throw new IOException("attempted write would go past the end of fixed size stream"); }

			while (count > 0 && currentstreamindex < streams.Count)
			{
				long numbytesleftinstream = positionlookup.Keys[currentstreamindex].end - Position;
				BAssert(numbytesleftinstream >= 0);
				int numbytestowrite = (int) Math.Min(numbytesleftinstream, count);
				streams[currentstreamindex].Write(buffer, offset, numbytestowrite);
				checked
				{
					offset += numbytestowrite;
					count -= numbytestowrite;
					_position += numbytestowrite;
				}

				if (count == 0) { break; }

				currentstreamindex += 1;
				if (CanSeek) { streams[currentstreamindex].Seek(0, SeekOrigin.Begin); }

			}
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			if (!CanSeek) { throw new NotSupportedException(); }
			if (disposedValue) { throw new ObjectDisposedException(nameof(ConcatenatedStream)); }
			switch (origin)
			{
				case SeekOrigin.Begin:
					Position = offset;
					return Position;
				case SeekOrigin.Current:
					Position += offset;
					return Position;
				case SeekOrigin.End:
					Position = Length + offset;
					return Position;
				default:
					throw new Exception($"unknown or not implemented SeekOrigin value: {origin}");
			}
		}


		public override void SetLength(long value)
		{
			if (disposedValue) { throw new ObjectDisposedException(nameof(ConcatenatedStream)); }
			throw new NotSupportedException($"{nameof(ConcatenatedStream)} class does not support resizing");
		}



		bool disposedValue = false;

		protected override void Dispose(bool disposing)
		{
			if (!disposedValue)
			{
				if (disposing)
				{
					foreach (var stream in streams) { stream?.Dispose(); }
					streams = null;
					positionlookup = null;
				}

				base.Dispose(disposing);
				disposedValue = true;
			}
		}
		

	}



	/// <summary>
	/// exception to throw for code that should be unreachable.
	/// </summary>
	public class UnreachableException : Exception
	{
		/// <summary>
		/// exception to throw for code that should be unreachable.
		/// </summary>
		public UnreachableException() : base("the compiler does not detect this code as unreachable") { }
	}



}




