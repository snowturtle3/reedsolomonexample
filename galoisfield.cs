using System;
using UtilNs;
using BufferNs;
using System.Runtime.CompilerServices;

namespace ReedSolomonNs
{


	public class GaloisField
	{
		/// <summary>
		/// a number with the overflow bit set (one bit higher than the highest bit for values in the field)
		/// </summary>
		public uint overflowmask { get; private set; }
		//note: generator to the (overflowmask-1) power = 1

		/// <summary>
		/// wraparound power; any nonzero number to this power is 1.
		/// </summary>
		public uint log_of_1 { get { return overflowmask - 1; } }

		public uint polynomial { get; private set; } //this poly is treated as being equal to 0
													 //the generator is assumed to be 2
		public int numbits { get; private set; }
		uint[] exp_lookup;
		uint[] log_lookup;

		//not needed because we can just use xor directly, but included for clarity
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint Add(uint n1, uint n2)
		{
			return n1 ^ n2;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint Neg(uint n)
		{
			return n;
		}

		uint MultiplyWithoutLookup(uint n1, uint n2)
		{
			uint r = 0;

			while (n1 != 0)
			{
				if ((n1 & 1) != 0)
				{
					r ^= n2;
				}
				n1 >>= 1;
				n2 <<= 1;
				if ((n2 & this.overflowmask) != 0) { n2 ^= this.polynomial; }
			}


			return r;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint Multiply(uint n1, uint n2)
		{
			if (n1 == 0 || n2 == 0) { return 0; }
			return exp_lookup[log_lookup[n1] + log_lookup[n2]];
		}

		public GaloisField(int numbits, uint polynomial)
		{
			if (numbits > 30 || numbits <= 1)
			//one bit would be valid (check should be (numbits <= 0)), but I can't be bothered to make the below code work with it
			{ throw new Exception($"Galois field size in bits too large: {numbits}"); }
			this.numbits = numbits;
			this.overflowmask = 1u << this.numbits;
			this.polynomial = polynomial;
			if ((polynomial & ~(overflowmask - 1)) != overflowmask)
			{ throw new Exception("invalid degree of polynomial"); }


			exp_lookup = new uint[overflowmask * 2];
			log_lookup = new uint[overflowmask];


			uint value = 1;
			for (uint power = 0; power < log_of_1; power++)
			{
				if (value == 1 && power != 0)
				{ throw new Exception($"not a primitive polynomial: {numbits} bit 0x{polynomial.ToString("x")}"); }

				log_lookup[value] = power;
				exp_lookup[power] = value;

				value = MultiplyWithoutLookup(value, 2); //assume the generator is 2

			}

			if (value != 1)
			{ throw new Exception($"not a primitive polynomial: {numbits} bit 0x{polynomial.ToString("x")}"); }


			//make a second copy so we can lookup exp(log n1 + log n2)
			Array.Copy(exp_lookup, 0, exp_lookup, log_of_1, log_of_1);



		}

		/// <summary>
		/// raises n to the given power
		/// </summary>
		public uint Power(uint n, uint pow)
		{
			if (n == 0)
			{
				if (pow == 0) { return 1; }
				else { return 0; }
			}

			return exp_lookup[
				(log_lookup[n] * pow) % log_of_1
				];
		}

		/// <summary>
		/// gets the number, inv, such that inv*n=1
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint MultInverse(uint n)
		{
			if (n == 0) { throw new Exception("divide by zero"); }

			return exp_lookup[
				log_of_1 - log_lookup[n]
			];
		}



		/// <summary>
		/// buffer *= factor
		/// </summary>
		/// <param name="datatypebytes">currently only supports 1 for byte or 2 for ushort. 
		/// Must be high enough to fit this.numbits. If it is higher, the unused high order bits must be zero.
		/// The size of the buffer must be a multiple of the datatype size.</param>
		public unsafe void MultiplyBlock(IBuffer buffer, uint factor, int datatypebytes)
		{

			if (this.numbits > datatypebytes * 8) { throw new Exception("data type too small"); }
			//if (this.numbits < datatypebytes * 8) { throw new Exception("warning, ignoring the high order bits"); }
			if (buffer.sizeinbytes % datatypebytes != 0) { throw new Exception("buffer size not a multiple of data type"); }
			if (!(datatypebytes == 1 || datatypebytes == 2)) { throw new Exception($"unsupported data type: {datatypebytes} bytes"); }


			if (factor == 0) { buffer.ZeroFill(); return; } //necessary "optimization" due to log(0) being undefined

			long count = buffer.sizeinbytes / datatypebytes;
			uint factorlog = log_lookup[factor];

			IntPtr block_locked = buffer.Lock(0, count, datatypebytes);


			//c# generics have weirdly terrible support for primitive integer operations
			if (datatypebytes == 1)
			{
				byte* blockptr = (byte*) block_locked;
				for (long i = 0; i < count; i++)
				{
					uint n = blockptr[i];
					if (n == 0) { blockptr[i] = 0; continue; }
					blockptr[i] = (byte) exp_lookup[log_lookup[n] + factorlog];
				}
			}
			else if (datatypebytes == 2)
			{
				ushort* blockptr = (ushort*) block_locked;
				for (long i = 0; i < count; i++)
				{
					uint n = blockptr[i];
					if (n == 0) { blockptr[i] = 0; continue; }
					blockptr[i] = (ushort) exp_lookup[log_lookup[n] + factorlog];
				}
			}
			else
			{
				//we tested for this above
				throw new UnreachableException();
			}


			buffer.Unlock();
		}




		/// <summary>
		/// dest += src * factor
		/// </summary>
		/// <param name="datatypebytes">currently only supports 1 for byte or 2 for ushort. 
		/// Must be high enough to fit this.numbits. If it is higher, the unused high order bits must be zero.
		/// The size of the buffer must be a multiple of the datatype size.</param>
		public unsafe void AddMultipleOfBlock(IBuffer dest, IBuffer src, uint factor, int datatypebytes)
		{
			if (factor == 0) { return; } //no-op
			if (this.numbits > datatypebytes * 8) { throw new Exception("data type too small"); }
			//if(this.numbits < datatypebytes * 8) { throw new Exception("warning, ignoring the high order bits"); }

			if (dest.sizeinbytes != src.sizeinbytes) { throw new Exception("mismatched buffer size"); }
			if (dest.sizeinbytes % datatypebytes != 0) { throw new Exception("buffer size not a multiple of data type"); }

			if (!(datatypebytes == 1 || datatypebytes == 2)) { throw new Exception($"unsupported data type: {datatypebytes} bytes"); }



			long count = dest.sizeinbytes / datatypebytes;
			uint factorlog = log_lookup[factor];

			IntPtr dest_locked = dest.Lock(0, count, datatypebytes);
			IntPtr src_locked = src.Lock(0, count, datatypebytes);


			//c# generics don't support primitive integer operations, so we have to do this copy paste nonsense
			if (datatypebytes == 1)
			{
				byte* destptr = (byte*) dest_locked;
				byte* srcptr = (byte*) src_locked;

				for (long i = 0; i < count; i++)
				{
					uint n = srcptr[i];
					if (n == 0) { continue; }
					destptr[i] ^= (byte) exp_lookup[log_lookup[n] + factorlog];
				}
			}
			else if (datatypebytes == 2)
			{
				ushort* destptr = (ushort*) dest_locked;
				ushort* srcptr = (ushort*) src_locked;

				for (long i = 0; i < count; i++)
				{
					uint n = srcptr[i];
					if (n == 0) { continue; }
					destptr[i] ^= (ushort) exp_lookup[log_lookup[n] + factorlog];
				}
			}
			else
			{
				//we tested for this possibility above, so this should never hit
				throw new UnreachableException();
			}



			dest.Unlock();
			src.Unlock();
		}



		/// <summary>
		/// dest += src * factor <para></para>
		/// This overload only supports 8 and 16 bit galois fields, and will select between byte and ushort datatype as appropriate.
		/// </summary>
		public void AddMultipleOfBlock(IBuffer dest, IBuffer src, uint factor)
		{
			switch (this.numbits)
			{
				case 8: AddMultipleOfBlock(dest, src, factor, 1); break;
				case 16: AddMultipleOfBlock(dest, src, factor, 2); break;
				default: throw new ArgumentException("this overload only supports 8 and 16 bit fields");
			}
		}


		/// <summary>
		/// buffer *= factor <para></para>
		/// This overload only supports 8 and 16 bit galois fields, and will select between byte and ushort datatype as appropriate.
		/// </summary>
		public void MultiplyBlock(IBuffer buffer, uint factor)
		{
			switch (this.numbits)
			{
				case 8: MultiplyBlock(buffer, factor, 1); break;
				case 16: MultiplyBlock(buffer, factor, 2); break;
				default: throw new ArgumentException("this overload only supports 8 and 16 bit fields");
			}
		}









		//	/// <summary>
		//	/// multiplies each byte in the block by factor
		//	/// </summary>
		//	/// <param name="block">array containing the block's data</param>
		//	/// <param name="factor">number to multiply each element in the block by</param>
		//	/// <param name="offset">the offset, in array elements, within the array that the block's data starts at</param>
		//	/// <param name="count">number of elements in the array that are part of the block, or -1 to indicate the entire rest of the array</param>
		//	public void MultiplyBlock(byte[] block, uint factor, int offset = 0, int count = -1)
		//	{
		//		if (numbits > 8) { throw new Exception($"galoisfield(2^{numbits} requires larger than 8bit elements"); } //uses item type
		//		if (block == null) { return; }
		//		if (factor == 0) { Array.Clear(block, 0, block.Length); return; }
		//		if (count == -1) { count = block.Length - offset; }
		//		if (count < 0 || offset < 0 || offset > block.Length || count > block.Length - offset)
		//		{
		//			throw new IndexOutOfRangeException();
		//		}
		//		
		//		
		//		
		//		uint factorlog = log_lookup[factor];
		//		
		//		for (int i = offset; i < offset + count; i++)
		//		{
		//			uint n = block[i];
		//			if (n == 0) { block[i] = 0; continue; }
		//			block[i] = (byte) exp_lookup[log_lookup[n] + factorlog];  //uses item type
		//		}
		//	}
		//	
		//	/// <summary>
		//	/// multiplies each UInt16 in the block by factor
		//	/// </summary>
		//	/// <param name="block">array containing the block's data</param>
		//	/// <param name="factor">number to multiply each element in the block by</param>
		//	/// <param name="offset">the offset, in array elements, within the array that the block's data starts at</param>
		//	/// <param name="count">number of elements in the array that are part of the block, or -1 to indicate the entire rest of the array</param>
		//	public void MultiplyBlock(UInt16[] block, uint factor, int offset = 0, int count = -1)
		//	{
		//		if (numbits > 16) { throw new Exception($"galoisfield(2^{numbits} requires larger than 16bit elements"); } //uses item type
		//		if (block == null) { return; }
		//		if (factor == 0) { Array.Clear(block, 0, block.Length); return; }
		//		if (count == -1) { count = block.Length - offset; }
		//		if (count < 0 || offset < 0 || offset > block.Length || count > block.Length - offset)
		//		{
		//			throw new IndexOutOfRangeException();
		//		}
		//		
		//		
		//		uint factorlog = log_lookup[factor];
		//		
		//		for (int i = offset; i < offset + count; i++)
		//		{
		//			uint n = block[i];
		//			if (n == 0) { block[i] = 0; continue; }
		//			block[i] = (UInt16) exp_lookup[log_lookup[n] + factorlog];  //uses item type
		//		}
		//	}
		//	
		//	
		//	
		//	/// <summary>
		//	/// <para>dest += src * factor </para>(operation performed per-byte)
		//	/// </summary>
		//	/// <param name="dest">array containing the dest block's data</param>
		//	/// <param name="src">array containing the source block's data</param>
		//	/// <param name="factor">number to multiply each element in the source block by</param>
		//	/// <param name="src_offset">the offset, in array elements, within the src array that the source block's data starts at</param>
		//	/// <param name="dest_offset">the offset, in array elements, within the dest array that the dest block's data starts at</param>
		//	/// <param name="count">number of elements in each array that are part of the block, or -1 to indicate the entire rest of the array</param>
		//	public void AddMultipleOfBlock(byte[] dest, byte[] src, uint factor, int dest_offset = 0, int src_offset = 0, int count = -1)
		//	{
		//		if (factor == 0) { return; }
		//		if (src == null) { return; }
		//		if (dest == null) { throw new Exception("destination block null"); }
		//		if (numbits > 8) { throw new Exception($"galoisfield(2^{numbits} requires larger than 8bit elements"); } //uses item type 
		//		
		//		
		//		if (count == -1)
		//		{
		//			if (dest.Length - dest_offset != src.Length - src_offset)
		//			{
		//				throw new ArgumentException("dest and source blocks different length with count = -1");
		//			}
		//			count = dest.Length - dest_offset;
		//		}
		//		
		//		if (count < 0 || dest_offset < 0 || dest_offset > dest.Length || count > dest.Length - dest_offset
		//					  || src_offset < 0 || src_offset > src.Length || count > src.Length - src_offset)
		//		{
		//			throw new IndexOutOfRangeException();
		//		}
		//		
		//	
		//		
		//		uint factorlog = log_lookup[factor];
		//		
		//		for (int i = 0; i < count; i++)
		//		{
		//			uint n = src[i + src_offset];
		//			if (n == 0) { continue; }
		//			dest[i + dest_offset] ^= (byte) exp_lookup[log_lookup[n] + factorlog]; //uses item type 
		//		}
		//		
		//	}
		//	
		//	/// <summary>
		//	/// <para>dest += src * factor </para>(operation performed per-UInt16)
		//	/// </summary>
		//	/// <param name="dest">array containing the dest block's data</param>
		//	/// <param name="src">array containing the source block's data</param>
		//	/// <param name="factor">number to multiply each element in the source block by</param>
		//	/// <param name="src_offset">the offset, in array elements, within the src array that the source block's data starts at</param>
		//	/// <param name="dest_offset">the offset, in array elements, within the dest array that the dest block's data starts at</param>
		//	/// <param name="count">number of elements in each array that are part of the block, or -1 to indicate the entire rest of the array</param>
		//	public void AddMultipleOfBlock(UInt16[] dest, UInt16[] src, uint factor, int dest_offset = 0, int src_offset = 0, int count = -1)
		//	{
		//		if (factor == 0) { return; }
		//		if (src == null) { return; }
		//		if (dest == null) { throw new Exception("destination block null"); }
		//		if (numbits > 16) { throw new Exception($"galoisfield(2^{numbits} requires larger than 16bit elements"); } //uses item type 
		//		
		//		
		//		if (count == -1)
		//		{
		//			if (dest.Length - dest_offset != src.Length - src_offset)
		//			{
		//				throw new ArgumentException("dest and source blocks different length with count = -1");
		//			}
		//			count = dest.Length - dest_offset;
		//		}
		//		
		//		if (count < 0 || dest_offset < 0 || dest_offset > dest.Length || count > dest.Length - dest_offset
		//					  || src_offset < 0 || src_offset > src.Length || count > src.Length - src_offset)
		//		{
		//			throw new IndexOutOfRangeException();
		//		}
		//		
		//		
		//		uint factorlog = log_lookup[factor];
		//		
		//		for (int i = 0; i < count; i++)
		//		{
		//			uint n = src[i + src_offset];
		//			if (n == 0) { continue; }
		//			dest[i + dest_offset] ^= (UInt16) exp_lookup[log_lookup[n] + factorlog]; //uses item type 
		//		}
		//		
		//	}


		public override bool Equals(object obj)
		{
			var other = obj as GaloisField;
			if (other == null) { return base.Equals(obj); }
			return other.numbits == this.numbits && other.polynomial == this.polynomial;
		}


		public override int GetHashCode()
		{
			return (int) polynomial;
		}



	}

	/// <summary>
	/// A matrix where every number is an element of a galois field. All operations done on or by the matrix (for example, 
	/// adding a multiple of a row to another row or multiplying the matrix times a vector) use the definitions of add and
	/// multiply defined by the galois field.
	/// </summary>
	public class GFMatrix
	{
		//each element of the matrix is an element of gf(2^n)

		public uint[] data { get; private set; }
		public int rowsize { get; private set; }
		public int colsize { get; private set; }

		public int numrows { get { return colsize; } private set { colsize = value; } }
		public int numcols { get { return rowsize; } private set { rowsize = value; } }
		public GaloisField field { get; private set; }

		public void Print()
		{
			for (int row = 0; row < numrows; row++)
			{
				for (int col = 0; col < numcols; col++)
				{
					Console.Write(this[row, col]);
					Console.Write(" ");
				}
				Console.Write("\n");
			}
		}

		public GFMatrix(int numrows, int numcols, GaloisField field)
		{
			data = new uint[numrows * numcols];
			this.numcols = numcols;
			this.numrows = numrows;
			this.field = field;

		}

		/// <summary>
		/// creates this matrix as a clone of an existing matrix
		/// </summary>
		/// <param name="m">the matrix to clone</param>
		public GFMatrix(GFMatrix m)
		{
			this.rowsize = m.rowsize;
			this.colsize = m.colsize;
			this.field = m.field;
			this.data = new uint[numrows * numcols];
			for (int i = 0; i < numrows * numcols; i++) { this.data[i] = m.data[i]; }

		}

		public GFMatrix(uint[] data, int numrows, int numcols, GaloisField field)
		{
			this.numrows = numrows;
			this.numcols = numcols;
			this.field = field;
			if (data.Length != numrows * numcols) { throw new Exception("wrong array size"); }
			this.data = new uint[data.Length];
			Array.Copy(data, this.data, data.Length);

			uint mask = ~(field.overflowmask - 1);
			for (int i = 0; i < data.Length; i++)
			{
				if ((data[i] & mask) != 0) { throw new Exception("data does not fit in field"); }
			}
		}

		public uint this[int row, int col]
		{
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			get
			{
				if (row >= numrows || col >= numcols || row < 0 || col < 0) { throw new Exception("matrix index out of bounds"); }
				return data[row * rowsize + col];
			}
			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			set
			{
				if (row >= numrows || col >= numcols || row < 0 || col < 0) { throw new Exception("matrix index out of bounds"); }
				data[row * rowsize + col] = value;
			}
		}

		public void GetRow(int n, uint[] data)
		{
			for (int i = 0; i < rowsize; i++) { data[i] = this[n, i]; }
		}

		public void GetCol(int n, uint[] data)
		{
			for (int i = 0; i < colsize; i++) { data[i] = this[i, n]; }
		}

		public uint[] GetRow(int n)
		{
			var r = new uint[rowsize];
			GetRow(n, r);
			return r;
		}

		public uint[] GetCol(int n)
		{
			var r = new uint[colsize];
			GetCol(n, r);
			return r;
		}

		public void SetCol(int n, uint[] data)
		{
			for (int i = 0; i < colsize; i++) { this[i, n] = data[i]; }
		}
		public void SetRow(int n, uint[] data)
		{
			for (int i = 0; i < rowsize; i++) { this[n, i] = data[i]; }
		}

		/// <summary>
		/// rows[dest] += rows[src] * factor
		/// </summary>
		public void RowOp_AddMultiple(int dest, int src, uint factor)
		{
			for (int i = 0; i < rowsize; i++)
			{
				this[dest, i] =
					field.Add(this[dest, i],
							  field.Multiply(this[src, i], factor)
							 );
			}
		}
		public void RowOp_Multiply(int dest, uint factor)
		{
			for (int i = 0; i < rowsize; i++)
			{
				this[dest, i] = field.Multiply(this[dest, i], factor);
			}
		}
		public void RowOp_Swap(int r1, int r2)
		{
			uint temp = 0;
			for (int i = 0; i < rowsize; i++)
			{
				temp = this[r1, i];
				this[r1, i] = this[r2, i];
				this[r2, i] = temp;
			}
		}

		/// <summary>
		/// cols[dest] += cols[src] * factor
		/// </summary>
		public void ColOp_AddMultiple(int dest, int src, uint factor)
		{
			for (int i = 0; i < colsize; i++)
			{
				this[i, dest] =
					field.Add(this[i, dest],
							  field.Multiply(this[i, src], factor)
							 );
			}
		}
		public void ColOp_Multiply(int dest, uint factor)
		{
			for (int i = 0; i < colsize; i++)
			{
				this[i, dest] = field.Multiply(this[i, dest], factor);
			}
		}
		public void ColOp_Swap(int c1, int c2)
		{
			uint temp = 0;
			for (int i = 0; i < colsize; i++)
			{
				temp = this[i, c1];
				this[i, c1] = this[i, c2];
				this[i, c2] = temp;
			}
		}


		/// <summary>
		/// for non square matrices, the extra space outside the top/left-most square will be zeros
		/// </summary>
		public void SetToIdentityMatrix()
		{
			int numberofones = Math.Min(rowsize, colsize);
			for (int i = 0; i < rowsize * colsize; i++) { data[i] = 0; }
			for (int i = 0; i < numberofones; i++) { this[i, i] = 1; }
		}


		/// <summary>
		/// Changes the matrix so that the leftmost square (numrows * numrows) is the identity matrix.
		/// Returns a matrix containing the operations applied, inv, such that inv * old_this = new_this
		/// </summary>
		/// <returns></returns>
		public GFMatrix ReduceAndGetInverse()
		{

			//this function reduces the leftmost square of the matrix to the identity and returns the matrix required to undo those changes.
			//for a square matrix input, this is just the inverse and this function was written from the perspective of just getting the inverse.
			//however this also works with the vandermonde generator and so has been moved to a different function.


			if (rowsize < colsize) { throw new Exception("function requires width at least equal to height"); }
			//it may or may not not actually require that, but was made with that assumption in mind


			var m = this;

			var inv = new GFMatrix(colsize, colsize, this.field); //return value. square matrix with rows and cols equal to colsize
			inv.SetToIdentityMatrix();


			//we want [inverse][m][vector] = [identity][vector]

			// procedure: do operations to m until you get the identity matrix. do the same ops to the identity matrix to get the inverse.
			//                            [m][vec] =                         [m]   [vec]
			// ( [op][op][op][identity] ) [m][vec] = ( [op][op][op][identity][m] ) [vec]
			// (  inverse               )            ( identity                  )


			//these operations must be row operations because they are on the left
			//note that matrix multiplication is associative but not commutative


			// eg.
			// 
			// swap
			//
			//   0 1 0     a b c       d e f 
			// ( 1 0 0 ) ( d e f ) = ( a b c )
			//   0 0 1     g h i       g h i 
			// 
			// 
			// add multiple
			//
			//   1 3 0     a b c       (a+3d) (b+3e) (c+3f)
			// ( 0 1 0 ) ( d e f ) = (     d      e      f  )
			//   0 0 1     g h i           g      h      i
			// 


			uint factor = 0;

			//note, by "identity slot" i mean the position within a row that would be one in the identity matrix

			for (int row = 0; row < m.numrows; row++)
			{
				//if the identity slot is zero, swap this row for a different row
				if (m[row, row] == 0)
				{
					for (int j = row + 1; j < m.numrows; j++)
					{
						if (m[j, row] != 0)
						{
							m.RowOp_Swap(row, j);
							inv.RowOp_Swap(row, j);
							break;
						}
					}
					if (m[row, row] == 0) { throw new Exception("matrix not invertible"); }
				}


				//make the identity slot be 1
				factor = m.field.MultInverse(m[row, row]);
				if(factor != 1)
				{
					m.RowOp_Multiply(row, factor);
					inv.RowOp_Multiply(row, factor);
				}


				//make the column below the identity slot be 0s
				for (int j = row + 1; j < m.numrows; j++)
				{
					factor = m.field.Neg(m[j, row]);
					if (factor == 0) { continue; }
					m.RowOp_AddMultiple(j, row, factor);
					inv.RowOp_AddMultiple(j, row, factor);
				}

			}


			//matrix is now upper triangular
			//   1 j k 
			//   0 1 p 
			//   0 0 1 


			//get zeroes in the remaining upper triangle slots
			for (int row = 1; row < m.numrows; row++)
			{
				for (int j = row - 1; j >= 0; j--)
				{
					factor = m.field.Neg(m[j, row]);
					if(factor == 0) { continue; }
					m.RowOp_AddMultiple(j, row, factor);
					inv.RowOp_AddMultiple(j, row, factor);
				}
			}



			return inv;

		}


		public GFMatrix GetInverse()
		{
			if (rowsize != colsize) { throw new Exception("attempt to get inverse of non square matrix"); }
			var m = new GFMatrix(this); //mutable copy of this matrix
			return m.ReduceAndGetInverse();

		}


		public GFMatrix GetTranspose()
		{
			var t = new GFMatrix(numcols, numrows, field);
			for (int row_this = 0; row_this < numrows; row_this++)
			{
				for (int col_this = 0; col_this < numcols; col_this++)
				{
					t[col_this, row_this] = this[row_this, col_this];
				}
			}
			return t;
		}


		/// <summary>
		/// gets a matrix, with the topmost square part (numcols*numcols) as the identity matrix,
		/// which is guaranteed to be invertible if rows are removed until it is square, regardless of which rows were removed
		/// </summary>
		/// <param name="numrows"></param>
		/// <param name="numcols"></param>
		/// <param name="field"></param>
		/// <returns></returns>
		public static GFMatrix GetReducedVandermondeMatrix(int numrows, int numcols, GaloisField field)
		{
			if (numcols > numrows) { throw new Exception("this library assumes rows >= cols"); }
			if (numrows > GetMaxBlocksForGaloisField(field)) { throw new Exception("too many rows for this galois field; the matrix would not be invertible"); }


			var m = new GFMatrix(numrows, numcols, field);

			for (int row = 0; row < m.numrows; row++)
			{
				for (int col = 0; col < m.numcols; col++)
				{
					m[row, col] = m.field.Power((uint) row, (uint) col);
				}
			}

			//the get inverse function was written for row operations, but this needs col operations
			var t = m.GetTranspose();
			t.ReduceAndGetInverse();
			return t.GetTranspose();
		}


		//this doesn't really belong here and should be moved to some other class
		public static int GetMaxBlocksForGaloisField(GaloisField gf)
		{
			return (int) gf.overflowmask;
		}


		/// <summary>
		/// adds a multiple of a column to a vector.
		/// accumulator += columns[col] * factor
		/// </summary>
		private void AccumulateColMultiple(uint[] accumulator, int col, uint factor)
		{
			for (int i = 0; i < colsize; i++)
			{
				accumulator[i] = field.Add(
					accumulator[i],
					field.Multiply(this[i, col], factor)
					);
			}
		}


		public void MatrixTimesVector(uint[] result, uint[] v)
		{
			if (v.Length != this.rowsize || result.Length != this.colsize) { throw new Exception("size mismatch"); }
			Array.Clear(result, 0, result.Length);


			for (int i = 0; i < rowsize; i++)
			{
				AccumulateColMultiple(result, i, v[i]);
			}
		}


		public uint[] MatrixTimesVector(uint[] v)
		{
			var r = new uint[colsize];
			MatrixTimesVector(r, v);
			return r;
		}


		/// <summary>
		/// computes m1*m2
		/// </summary>
		public static GFMatrix MatrixMultiply(GFMatrix m1, GFMatrix m2)
		{
			if (!(m1.field.Equals(m2.field))) { throw new Exception("can't multiply matrices with different galois fields"); }
			if (m2.colsize != m1.rowsize) { throw new Exception("size mismatch for multiplying"); }


			GFMatrix r = new GFMatrix(m1.numrows, m2.numcols, m1.field);

			//var v3 = new uint[m1.colsize]; //vector which will be the current output column

			for (int m2col = 0; m2col < m2.numcols; m2col++)
			{
				//Array.Clear(v3, 0, v3.Length);

				r.SetCol(m2col,
					m1.MatrixTimesVector(m2.GetCol(m2col))
					);
			}

			return r;

		}


		public static uint VectorDot(uint[] v1, uint[] v2, GaloisField field)
		{
			if (v1.Length != v2.Length) { throw new ArgumentException("arrays not the same length"); }
			int length = v1.Length;
			uint r = 0;
			for (int i = 0; i < length; i++)
			{
				r = field.Add(r, field.Multiply(v1[i], v2[i]));
			}
			return r;
		}


		/// <param name="dest">can be the same as v1 or v2</param>
		public static void VectorAdd(uint[] dest, uint[] v1, uint[] v2, GaloisField field)
		{
			int length = dest.Length;
			if (v1.Length != length || v2.Length != length) { throw new ArgumentException("arrays not the same length"); }

			for (int i = 0; i < length; i++)
			{
				dest[i] = field.Add(v1[i], v2[i]);
			}
		}

		/// <summary>
		/// dest += v * factor
		/// </summary>
		public static void VectorAddMultiple(uint[] dest, uint[] v, uint factor, GaloisField field)
		{
			int length = dest.Length;
			if (v.Length != length) { throw new ArgumentException("arrays not the same length"); }

			for (int i = 0; i < length; i++)
			{
				dest[i] = field.Add(dest[i],
									field.Multiply(v[i], factor)
									);
			}
		}


		public static void VectorMultiplyScalar(uint[] v, uint factor, GaloisField field)
		{
			int length = v.Length;

			for (int i = 0; i < length; i++)
			{
				v[i] = field.Multiply(v[i], factor);
			}
		}





	}

}