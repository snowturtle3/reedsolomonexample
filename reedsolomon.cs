using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static ReedSolomonNs.RSAssertUtil;
using UtilNs;
using BufferNs;






namespace ReedSolomonNs
{
	using Block = RSBlock;
	using BlockType = RSBlockType;
	

	public static class RSAssertUtil
	{

		public static void RsAssert(bool b, string msg = "")
		{
			//"Reed Solomon Library Assertion Failed"

			if (!b) { throw new RSAssertionFailedException(msg); }
		}
	}

	
	public class RsNotEnoughBlocksException : Exception
	{
		public RsNotEnoughBlocksException(string msg="") : base(msg) { }
		public RsNotEnoughBlocksException(Exception innerexception) : base("", innerexception) { }

		public int numblockshave, numblocksneed;

		public RsNotEnoughBlocksException(int numblockshave, int numblocksneed)
			:base($"not enough blocks to recover: have {numblockshave} of {numblocksneed} needed")
		{
			this.numblockshave = numblockshave;
			this.numblocksneed = numblocksneed;
		}
	}




	public enum RSBlockType
	{

		//even though there are only 2 possibilities (data and parity) that could be handled by a single bit,
		//data and parity aren't really opposites. 
		//writing, say, (Data | NeedsGenerating) is more clear than having (Data) be zero and be implied from the lack of Parity flag set.
		//So use a separate flag for each.
		//this also allows checking for zero as an invalid/uninitialized state.

		Data = 1,
		Parity = 2,

		/// <summary>
		/// set if the block is missing and needs to be recovered, or for parity blocks that haven't yet been generated the first time. 
		/// clear if the data is intact and exists in the same state as it was immediately after generating parity blocks.
		/// </summary>
		NeedsGenerating = 4,

		/// <summary>
		/// as an optimization, ignore the block's buffer (it can be null) and treat it as being zero-filled
		/// </summary>
		ZeroFilled = 8
	}


	/// <summary>
	/// base class for all reed solomon library exceptions
	/// </summary>
	public class RSException: Exception
	{
		public RSException(string msg = "") : base(msg) { }
		public RSException(Exception innerexception) : base("", innerexception) { }
		public RSException(string msg, Exception innerexception) : base(msg, innerexception) { }

	}
	

	/// <summary>
	/// fatal error in the reed solomon library
	/// </summary>
	public class RSAssertionFailedException: RSException
	{
		public RSAssertionFailedException(string msg = "") : base(msg) { }
		public RSAssertionFailedException(Exception innerexception) : base("", innerexception) { }
		public RSAssertionFailedException(string msg, Exception innerexception) : base(msg, innerexception) { }

	}
	
		

	public static class RSBlockTypeUtil
	{

		public static void AssertIsValid(this RSBlockType blocktype)
		{
			RSBlockType a = blocktype & (RSBlockType.Data | RSBlockType.Parity);
			RsAssert(
				a == RSBlockType.Data || a == RSBlockType.Parity,
				"data vs parity blocktype not initialized correctly; must be exactly one of those "
				);

			RsAssert(
				!(blocktype.HasFlag(RSBlockType.NeedsGenerating) && blocktype.HasFlag(RSBlockType.ZeroFilled)),
				$"block cannot be both {RSBlockType.NeedsGenerating}, which implies you don't know its contents," + 
				$"and {RSBlockType.ZeroFilled}, which implies you know that its contents is zeroes"
				);

			RsAssert(
				!(blocktype.HasFlag(RSBlockType.Parity) && blocktype.HasFlag(RSBlockType.ZeroFilled)),
				"assuming a parity block is zerofilled makes no sense"
				);

		}

		public static RSBlockType SetFlag(this RSBlockType blocktype, RSBlockType flag, bool hasflag = true)
		{
			return (blocktype & ~flag) | (hasflag ? flag : 0);
		}

		public static RSBlockType ClearFlag(this RSBlockType blocktype, RSBlockType flag)
		{
			return blocktype.SetFlag(flag, false);
		}


	}


	public class RSBlock
	{




		public bool IsDataBlock()
		{
			return blocktype.HasFlag(RSBlockType.Data);
		}

		public bool IsParityBlock()
		{
			return blocktype.HasFlag(RSBlockType.Parity);
		}

		/// <summary>
		/// true if the block's data is still intact and can be used for recovery
		/// </summary>
		public bool IsIntact()
		{
			return !IsProcessingNeeded();
		}

		public bool IsProcessingNeeded()
		{
			return blocktype.HasFlag(RSBlockType.NeedsGenerating);
		}

		public bool NeedsRecovery()
		{
			return IsProcessingNeeded();
		}

		public bool IsZeroFilled()
		{
			return blocktype.HasFlag(RSBlockType.ZeroFilled);
		}


		public void SetTypeFlag(RSBlockType flag, bool value = true)
		{
			blocktype = blocktype.SetFlag(flag, value);
		}


		public void ClearTypeFlag(RSBlockType flag)
		{
			SetTypeFlag(flag, false);
		}

		public bool HasTypeFlag(RSBlockType flag)
		{
			return blocktype.HasFlag(flag);
		}

		/// <summary>
		/// returns true if the buffer is null or zero length
		/// </summary>
		/// <returns></returns>
		public bool IsZeroLength()
		{
			return buffer == null || buffer.sizeinbytes == 0;
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="array">array containing the block's data</param>
		/// <param name="index">Index of the block within the list of all blocks in the stripe. The first data block has index 0. 
		/// The first parity block has index one more than the last data block's index (which is equal to the number of data blocks.)</param>
		/// <param name="blocktype"></param>
		/// <param name="byteoffset">the offset, in bytes, within the array that the block's data starts at</param>
		/// <param name="bytecount">number of bytes in the array that are part of the block, or -1 to indicate the entire rest of the array</param>
		/// 
		public RSBlock(byte[] array, int index, BlockType blocktype, int byteoffset = 0, int bytecount = -1)
		{
			this.index = index;
			this.blocktype = blocktype;

			if(array == null)
			{
				this.buffer = null;
			}
			else
			{
				if (bytecount == -1)
				{
					bytecount = array.Length * Util.SizeOfArrayElement(array);
				}

				buffer = (new ArrayBackedBuffer(array)).GetSlice(byteoffset, bytecount);
			}

		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="array">array containing the block's data</param>
		/// <param name="index">Index of the block within the list of all blocks in the stripe. The first data block has index 0. 
		/// The first parity block has index one more than the last data block's index (which is equal to the number of data blocks.)</param>
		/// <param name="blocktype"></param>
		/// <param name="byteoffset">the offset, in bytes, within the array that the block's data starts at. Must be divisible
		/// by the size of the array element.</param>
		/// <param name="bytecount">number of bytes in the array that are part of the block, or -1 to indicate the entire rest of the array. Must be divisible
		/// by the size of the array element if it is not -1.</param>
		/// 
		public RSBlock(ushort[] array, int index, BlockType blocktype, int byteoffset = 0, int bytecount = -1)
		{
			this.index = index;
			this.blocktype = blocktype;

			if(array == null)
			{
				this.buffer = null;
			}
			else
			{
				if (bytecount == -1)
				{
					bytecount = array.Length * Util.SizeOfArrayElement(array);
				}

				if ((byteoffset & 1) != 0 || (bytecount & 1) != 0)
				{
					throw new ArgumentException("byte numbers not a multiple of 2");
				}

				buffer = (new ArrayBackedBuffer(array)).GetSlice(byteoffset, bytecount);
			}
			

		}



		/// <summary>
		/// 
		/// </summary>
		/// <param name="buffer">array containing the block's data</param>
		/// <param name="index">Index of the block within the list of all blocks in the stripe. The first data block has index 0. 
		/// The first parity block has index one more than the last data block's index (which is equal to the number of data blocks.)</param>
		/// <param name="blocktype"></param>
		/// 
		public RSBlock(IBuffer buffer, int index, BlockType blocktype)
		{
			this.index = index;
			this.blocktype = blocktype;
			this.buffer = buffer;
		}

		



		private IBuffer _buffer;
		
		//currently not threadsafe ;would need guards on it
		public IBuffer buffer
		{
			get { return _buffer; }
			set
			{
				//_buffer?.Dispose(); //todo do something with refcounting
				_buffer = value;
			}
		}


		private BlockType _blocktype;

		public BlockType blocktype
		{
			get { return _blocktype; }
			set { value.AssertIsValid(); _blocktype = value; }
		}


		/// <summary>
		/// the first data block has index 0; the first parity block has index equal to the number of data blocks
		/// </summary>
		public int index;



	}



	public class ReedSolomon
	{
		//list of primitive polynomials http://web.eecs.utk.edu/~plank/plank/papers/CS-07-593/primitive-polynomial-table.txt

		public static readonly GaloisField gf4 = new GaloisField(4, 0x13);
		public static readonly GaloisField gf8 = new GaloisField(8, 0x11d);
		public static readonly GaloisField gf16 = new GaloisField(16, 0x1100b);



		/// <summary>
		/// Generates parity blocks, such that out of the data+parity blocks, any (numparityblocks) blocks can be 
		/// destroyed while still being recoverable.
		/// </summary>
		/// <param name="blocks">Complete list of all blocks. Parity blocks must be initially zero filled.</param>
		/// <param name="field">The Galois field to use.</param>
		public static void GenerateParityBlocks(IEnumerable<Block> blocks, GaloisField field)
		{
			var datablocks = new List<Block>();
			var parityblocks = new List<Block>();

			foreach (var block in blocks)
			{
				if (block.IsDataBlock()) { datablocks.Add(block); }
				else if (block.IsParityBlock()) { parityblocks.Add(block); }
				else { throw new Exception("unknown block type"); }
			}

			GenerateParityBlocks(datablocks, parityblocks, field);

		}



		/// <summary>
		/// This overload is guaranteed to only iterate through the data blocks once. For example, if datablocks wraps a class
		/// that loads each block from disk as needed, and the number of parity blocks is small enough to easily fit in 
		/// memory, each block would only be read from disk once.
		/// </summary>
		/// <param name="datablocks">A complete list of all data blocks, which should all be valid. Will not be changed by this function.</param>
		/// <param name="parityblocks">A complete list of all parity blocks, which will be fully generated by this function.
		/// Each parity block's buffer should be initially zero-filled when starting processing.</param>
		/// <param name="resumeinfo">Returned from BeginGenerateParityBlocksPartial().</param>
		/// <param name="field">The Galois field to use.</param>
		public static void GenerateParityBlocks(IList<Block> datablocks, IList<Block> parityblocks, GaloisField field)
		{
			var resumeinfo = BeginGenerateParityBlocksPartial(datablocks.Count, parityblocks.Count, field);
			GenerateParityBlocksPartial(datablocks, parityblocks, resumeinfo);

		}


		/// <summary>
		/// This overload is guaranteed to only iterate through the data blocks once. For example, if datablocks is an iterator 
		/// function that loads each block from disk as needed, and the number of parity blocks is small enough to easily fit in 
		/// memory, each block would only be read from disk once.
		/// </summary>
		/// <param name="datablocks">A complete list of all data blocks, which should all be valid. Will not be changed by this function.</param>
		/// <param name="numdatablocks">The number of data blocks, as a separate parameter to avoid needing to iterate through the list and count them.</param>
		/// <param name="parityblocks">A complete list of all parity blocks, which will be fully generated by this function.
		/// Each parity block's buffer should be initially zero-filled when starting processing.</param>
		/// <param name="numparityblocks">The number of parity blocks, as a separate parameter to avoid needing to iterate through the list and count them.</param>
		/// <param name="field">The Galois field to use</param>
		public static void GenerateParityBlocks(IEnumerable<Block> datablocks, int numdatablocks, IEnumerable<Block> parityblocks, int numparityblocks, GaloisField field)
		{
			var resumeinfo = BeginGenerateParityBlocksPartial(numdatablocks, numparityblocks, field);
			GenerateParityBlocksPartial(datablocks, parityblocks, resumeinfo);

		}


		/// <summary>
		/// GenerateParityBlocksPartial() generates recovery blocks without needing to have all of them in memory at the same time.
		/// Call this function first to get the structure to pass to GenerateParityBlocksPartial().
		/// </summary>
		/// <param name="numdatablocks"></param>
		/// <param name="numparityblocks"></param>
		/// <param name="field">Galois field to use. Leave null to automatically select an appropriate one.</param>
		/// <param name="datatypebytes">If using a galois field that is 8 or 16 bits, you can leave this as default to 
		/// automatically select 1 or 2 byte datatype. Otherwise, it must be set to a value high enough to fit the bits in. Note that the
		/// unused high order bits, if there are any, must be zero in every buffer passed to any generation or recovery function.</param>
		public static RSGenerationInfo BeginGenerateParityBlocksPartial(int numdatablocks, int numparityblocks, GaloisField field, int datatypebytes = -1)
		{
			int numtotalblocks = numdatablocks + numparityblocks;
			if (field == null)
			{
				if (numtotalblocks <= GFMatrix.GetMaxBlocksForGaloisField(gf8)) { field = gf8; }
				else { field = gf16; }
			}
			if (numtotalblocks > GFMatrix.GetMaxBlocksForGaloisField(field))
			{ throw new Exception($"too many total blocks for galois field: {numtotalblocks} blocks vs max of {GFMatrix.GetMaxBlocksForGaloisField(field)}"); }


			var r = new RSGenerationInfo();
			r.numdatablocks = numdatablocks;
			r.numparityblocks = numparityblocks;
			r.generationmatrix = GFMatrix.GetReducedVandermondeMatrix(numtotalblocks, numdatablocks, field);

			if(datatypebytes == -1)
			{
				switch (field.numbits)
				{
					case 8: datatypebytes = 1; break;
					case 16: datatypebytes = 2; break;
					default: throw new ArgumentException("must manually set a number of bytes for non 8 or 16 bit");
				}
			}
			r.datatypebytes = datatypebytes;

			return r;
		}




		/// <summary>
		/// This overload is guaranteed to only iterate through the data blocks once. For example, if datablocks is an iterator 
		/// function that loads each block from disk as needed, and the number of parity blocks is small enough to easily fit in 
		/// memory, each block would only be read from disk once.<para>
		/// This overload does the "real" work; the others are convenience wrappers.</para>
		/// </summary>
		/// <param name="datablocks">Must contain only valid data blocks. Each data block will be unchanged by this function.</param>
		/// <param name="parityblocks">Must contain only parity blocks, which will be updated by this function.
		/// When starting generation, each block's array should be zero-filled.</param>
		/// <param name="resumeinfo">Returned from BeginGenerateParityBlocksPartial().</param>
		public static void GenerateParityBlocksPartial(
			IEnumerable<Block> datablocks,
			IEnumerable<Block> parityblocks,
			ReedSolomon.RSGenerationInfo resumeinfo)
		{
			// 
			// | 1 0 0 |          | d1 |
			// | 0 1 0 | | d1 |   | d2 |
			// | 0 0 1 | | d2 | = | d3 |
			// | a b c | | d3 |   | p1 |
			// | d e f |          | p2 |
			// 
			// 


			foreach (var datablock in datablocks)
			{
				RsAssert(datablock.IsDataBlock(), 
					$"non data block passed to {nameof(datablocks)}");
				if (datablock.blocktype.HasFlag(BlockType.ZeroFilled))
					{ continue; } //adding a multiple of a zero filled block is a no-op
				RsAssert(datablock.IsIntact(),
					$"needs processing block passed to {nameof(datablocks)}");


				int matrixcol = datablock.index; //see matrix diagram above

				RsAssert(datablock.index < resumeinfo.numdatablocks && matrixcol < resumeinfo.generationmatrix.numcols,
					$"block index out of bounds: data block {datablock.index}");
				RsAssert(!datablock.IsZeroLength(),
					$"buffer in data block (index {datablock.index}) null");



				foreach (var parityblock in parityblocks)
				{
					RsAssert(parityblock.IsParityBlock(),
						$"non parity block passed to {nameof(parityblocks)}");

					int matrixrow = parityblock.index;

					RsAssert(parityblock.index >= resumeinfo.numdatablocks && parityblock.index < resumeinfo.numtotalblocks && matrixrow < resumeinfo.generationmatrix.numrows,
						$"block index out of bounds: parity block {parityblock.index}");
					RsAssert(!parityblock.IsZeroLength(),
						$"buffer in parity block (index {parityblock.index}) null");
					RsAssert(parityblock.buffer.sizeinbytes == datablock.buffer.sizeinbytes,
						$"block size mismatch: blocks index {datablock.index} and {parityblock.index}");
					



					resumeinfo.field.AddMultipleOfBlock(parityblock.buffer, datablock.buffer, resumeinfo.generationmatrix[matrixrow, matrixcol], resumeinfo.datatypebytes);
					
					

				}

			}

		}


		/// <summary>
		/// <para>Generates parity blocks without needing to have all blocks in memory at the same time. 
		/// Technically only one data block and one parity block at a time need to be in memory.</para>
		/// <para>Begin by passing in parity block(s) which are initially zero with some (or all) data blocks.
		/// Then continue passing the modified parity blocks in with different data blocks. </para>
		/// <para>A parity block will be completely generated once it has been passed in along with each different data block once.</para>
		/// <para>Do not pass a parity and data block together if those two have been passed together previously.</para>
		/// <para>Blocks are independent; eg. calling this function with (data blocks a,b,c and parity blocks d,e)
		/// is the same as calling once with (a,b,c and d), once with (a,b and e), and once with (c and e).</para>
		/// <para>Data blocks which are entirely zero-filled do not need to be passed in; passing them in would
		/// be a waste of processing time. You can pass in blocks marked with RSBlockType.ZeroFilled
		/// to indicate that the block contains only zeroes and need not be processed.</para>
		/// <para>This function does not keep track of which combinations of blocks have been passed in previously, 
		/// or whether the parity blocks are done being generated.</para>
		/// </summary>
		/// <param name="blocks"></param>
		/// <param name="resumeinfo">Information returned by BeginGenerateParityBlocksPartial(). 
		/// Is not modified and does not keep track of what blocks were passed in.</param>
		public static void GenerateParityBlocksPartial(
			IEnumerable<Block> blocks,
			ReedSolomon.RSGenerationInfo resumeinfo)
		{
			var datablocks = new List<Block>();
			var parityblocks = new List<Block>();

			foreach (var block in blocks)
			{
				if (block.IsDataBlock()) { datablocks.Add(block); }
				else if (block.IsParityBlock()) { parityblocks.Add(block); }
				else { RsAssert(false, "invalid blocktype somehow assigned"); }
			}

			GenerateParityBlocksPartial(datablocks, parityblocks, resumeinfo);
		}




		public static void RecoverDataBlocks(IList<bool> haveblocks, IEnumerable<Block> presentblocks, IEnumerable<Block> missingdatablocks,
			int original_numdatablocks, int original_numparityblocks, GaloisField field)
		{
			var resumeinfo = BeginRecoverDataBlocksPartial(haveblocks, original_numdatablocks, original_numparityblocks, field);
			RecoverDataBlocksPartial(presentblocks, missingdatablocks, resumeinfo);
		}


		/// <summary>
		/// 
		/// </summary>
		/// <param name="blocks">a complete list of all blocks</param>
		/// <param name="numdatablocks">original number of data blocks</param>
		/// <param name="numparityblocks">number of parity blocks originally created</param>
		/// <param name="field">Galois field to use. Must be the same as was used when creating.</param>
		/// <param name="alsorecoverparity">if false, only recovers data blocks</param>
		public static void RecoverDataBlocks(IEnumerable<Block> blocks, int numdatablocks, int numparityblocks, 
			GaloisField field, bool alsorecoverparity = true)
		{
			var haveblocks = new List<bool>();
			int totalblocks = numdatablocks + numparityblocks;
			for (int i = 0; i < totalblocks; i++) { haveblocks.Add(false); }
			var presentblocks = new List<Block>();
			var missingblocks = new List<Block>();

			foreach (var block in blocks)
			{
				if (block.IsIntact())
				{
					haveblocks[block.index] = true;
					presentblocks.Add(block);
				}
				else if (alsorecoverparity || block.IsDataBlock())
				{
					missingblocks.Add(block);
				}
			}

			var resumeinfo = BeginRecoverDataBlocksPartial(haveblocks, numdatablocks, numparityblocks, field, alsorecoverparity: alsorecoverparity);
			RecoverDataBlocksPartial(presentblocks, missingblocks, resumeinfo);
		}





		/// <summary>
		/// RecoverDataBlocksPartial() recovers blocks without needing to have all of them in memory at the same time. 
		/// Call this function first to get the structure to pass to RecoverDataBlocksPartial().
		/// <para>If you have more blocks than needed, the extra parity blocks will be ignored.</para>
		/// </summary>
		/// <param name="haveblocks">Each bool should be true if the block is still intact and usable for recovery, 
		/// or false if it is missing and needs to be recovered. 
		/// <para>The first data block has index 0. The first parity block has index
		/// equal to the number of data blocks. The total number of bools should be the sum of the data and parity blocks.</para></param>
		/// <param name="numdatablocks">the original number of data blocks</param>
		/// <param name="numparityblocks">the original number of parity blocks</param>
		/// <param name="gf">Galois field to use.</param>
		/// <param name="datatypebytes">If using a galois field that is 8 or 16 bits, you can leave this as default to 
		/// automatically select 1 or 2 byte datatype. Otherwise, it must be set to a value high enough to fit the bits in. Note that the
		/// unused high order bits, if there are any, must be zero in every buffer passed to any generation or recovery function.</param>
		/// <param name="alsorecoverparity">if false, only recovers data blocks</param>
		/// <returns></returns>
		public static RSRecoveryInfo BeginRecoverDataBlocksPartial(IList<bool> haveblocks, int numdatablocks, int numparityblocks, GaloisField gf,
			int datatypebytes = -1, bool alsorecoverparity = true)
		{

			//    | original generation matrix
			//	  v
			//   
			// | 1 0 0 |          | d1 |
			// | 0 1 0 | | d1 |   | d2 |
			// | 0 0 1 | | d2 | = | d3 |
			// | a b c | | d3 |   | p1 |
			// | d e f |          | p2 |
			// 
			// 

			// rows erased 

			// 
			// | 1 0 0 |          | d1 |
			//           | d1 |        |
			// | 0 0 1 | | d2 | = | d3 |
			//         | | d3 |        |
			// | d e f |          | p2 |
			// 
			// 


			// 
			// | 1 0 0 | | d1 |   | d1 |
			// | 0 0 1 | | d2 | = | d3 |
			// | d e f | | d3 |   | p2 |
			// 
			//

			// multiply the inverse of the matrix times both sides 


			// 
			//                 recovery vector |
			//                                 |
			//       resumeinfo.matrix |       |
			//                         v       v
			// 
			//            | d1 |   | x x x | | d1 |
			//            | d2 | = | x x x | | d3 |
			//            | d3 |   | x x x | | p2 |
			//  
			//  


			int numtotalblocks = numdatablocks + numparityblocks;
			RsAssert(haveblocks.Count == numdatablocks + numparityblocks, "block count mismatch");

			if (gf == null)
			{
				if (numtotalblocks <= GFMatrix.GetMaxBlocksForGaloisField(gf8)) { gf = gf8; }
				else { gf = gf16; }
			}
			RsAssert(numtotalblocks <= GFMatrix.GetMaxBlocksForGaloisField(gf),
				$"too many total blocks for galois field: {numtotalblocks} blocks vs max of {GFMatrix.GetMaxBlocksForGaloisField(gf)}");



			var r = new RSRecoveryInfo();
			r.original_numdatablocks = numdatablocks;
			r.original_numparityblocks = numparityblocks;
			switch (gf.numbits)
			{
				case 8: r.datatypebytes = 1; break;
				case 16: r.datatypebytes = 2; break;
				default: RsAssert(false, "unknown number of bits"); break;
			}



			var originalmatrix = GFMatrix.GetReducedVandermondeMatrix(numtotalblocks, numdatablocks, gf);
			var rowserasedmatrix = new GFMatrix(numdatablocks, numdatablocks, gf);

			int rowserasedmatrix_row = 0;

			for (int i = 0; i < haveblocks.Count && rowserasedmatrix_row < rowserasedmatrix.numrows; i++)
			{
				if (!haveblocks[i]) { continue; }

				rowserasedmatrix.SetRow(rowserasedmatrix_row, originalmatrix.GetRow(i));
				r.RecoveryVectorPositions.Add(i, rowserasedmatrix_row);
				rowserasedmatrix_row++;
			}

			if (rowserasedmatrix_row != rowserasedmatrix.numrows)
				{ throw new RsNotEnoughBlocksException(rowserasedmatrix_row, rowserasedmatrix.numrows); }

			r.matrix = rowserasedmatrix.GetInverse();


			// recovering parity at the same time as data


			//                                                    recovery vector |
			//                                                                    |
			//                                            recovery matrix |       |
			//                                                            |       |
			//                       original generation matrix |         |       |
			//                                                  v         v       v
			//                                 
			//                 | d1 |   | 1 0 0 | | d1 |    | 1 0 0 | | x x x | | d1 |
			//                 | d2 |   | 0 1 0 | | d2 | =  | 0 1 0 | | x x x | | d3 |
			//                 | d3 | = | 0 0 1 | | d3 |    | 0 0 1 | | x x x | | p2 |
			//                 | p1 |   | a b c |           | a b c |                   
			//                 | p2 |   | d e f |           | d e f |                  
			//                          
			//                  


			if (alsorecoverparity) { r.matrix = GFMatrix.MatrixMultiply(originalmatrix, r.matrix); }
			r.recoveringparity = alsorecoverparity;
			return r;

		}







		/// <summary>
		/// This overload is guaranteed to only iterate through the present blocks once. For example, if blockspresent is an iterator 
		/// function that loads each block from disk as needed, and the number of blocks that need recovering is small enough to easily fit in 
		/// memory, each block would only be read from disk once.<para>
		/// This overload does the "real" work; the others are convenience wrappers.</para>
		/// </summary>
		/// <param name="blockspresent">Must contain only blocks (data or parity) that are still intact</param>
		/// <param name="blockstorecover">Must contain only blocks that need recovering. If parity blocks are included, BeginRecover()
		/// must not have been called with the option disabled.</param>
		/// <param name="resumeinfo">returned from BeginRecoverDataBlocksPartial()</param>
		public static void RecoverDataBlocksPartial(
			IEnumerable<Block> blockspresent,
			IEnumerable<Block> blockstorecover,
			RSRecoveryInfo resumeinfo)
		{

			//see comments in BeginRecoverDataBlocksPartial for source of this matrix equation

			// 
			//                 recovery vector |
			//                                 |
			//       resumeinfo.matrix |       |
			//                         v       v
			// 
			//            | d1 |   | x x x | | d1 |
			//            | d2 | = | x x x | | d3 |
			//            | d3 |   | x x x | | p2 |
			//            | p1 |   | x x x | 
			//            | p2 |   | x x x | 
			//
			//



			foreach (var presentblock in blockspresent)
			{
				if (!presentblock.IsIntact())
					{ throw new RSAssertionFailedException("block without valid data"); }

				int col;
				if (!resumeinfo.RecoveryVectorPositions.TryGetValue(presentblock.index, out col))
					{ continue; } //block extraneous and not needed for recovery
				if (presentblock.IsZeroFilled())
					{ continue; } //adding a zero filled block is a no-op
				if (presentblock.IsZeroLength())
					{ throw new NullReferenceException("null data block not marked as zero filled"); }

				foreach (var blocktorecover in blockstorecover)
				{
					int row = blocktorecover.index;
					if (blocktorecover.IsDataBlock())
					{
						RsAssert(row >= 0 && row < resumeinfo.original_numdatablocks, "not a valid data block index");
					}
					else if (blocktorecover.IsParityBlock())
					{
						RsAssert(resumeinfo.recoveringparity, "attempt to recover parity when the appropriate flag was not set in BeginRecover()");
						RsAssert(row >= resumeinfo.original_numdatablocks && row < resumeinfo.original_numblocks, "not a valid parity block index");
					}
					else { throw new UnreachableException(); }

					
					if (!blocktorecover.NeedsRecovery())
						{ throw new RSAssertionFailedException("not a block that needs recovering"); }
					if (blocktorecover.IsZeroLength())
						{ throw new RSAssertionFailedException("null block"); }
					if (blocktorecover.buffer?.sizeinbytes != presentblock.buffer.sizeinbytes)
						{ throw new RSAssertionFailedException("block size mismatch"); }


					RsAssert(row >= 0 && row < resumeinfo.matrix.numrows && col >= 0 && col < resumeinfo.matrix.numcols,
						"invalid matrix coordinates"
						);



					resumeinfo.field.AddMultipleOfBlock(blocktorecover.buffer, presentblock.buffer, resumeinfo.matrix[row, col], resumeinfo.datatypebytes);

				}
			}

		}


		public static void RecoverDataBlocksPartial(
			IEnumerable<Block> blocks,
			RSRecoveryInfo resumeinfo)
		{
			var blockspresent = new List<Block>();
			var blockstorecover = new List<Block>();

			foreach (var block in blocks)
			{
				if (block.IsIntact()) { blockspresent.Add(block); }
				else if (block.NeedsRecovery() && (resumeinfo.recoveringparity || block.IsDataBlock())) { blockstorecover.Add(block); }
			}

			RecoverDataBlocksPartial(blockspresent, blockstorecover, resumeinfo);
		}


		/// <summary>
		/// information required for incremental block generation. Is not modified by the generation process and can be reused for multiple stripes.
		/// </summary>
		public class RSGenerationInfo
		{
			/// <summary>
			/// multiply this matrix times the vector of data blocks to get a concatenated vector of data+parity blocks.
			/// </summary>
			public GFMatrix generationmatrix;
			public int numparityblocks = 0, numdatablocks = 0;
			public int numtotalblocks { get { return numparityblocks + numdatablocks; } }
			public GaloisField field { get { return generationmatrix.field; } }

			/// <summary>
			/// 1 or 2 for byte or ushort for gf8 or gf16. 
			/// if using a galois field other than 8 or 16 bit, this value must be large enough, and 
			/// the unused high order bits of every buffer must be zero.
			/// </summary>
			public int datatypebytes;
		}


		/// <summary>
		/// information required for incremental block recovery. Is not modified by the recovery process and can be reused for multiple stripe segments.
		/// </summary>
		public class RSRecoveryInfo
		{
			/// <summary>
			/// multiply this matrix times the vector of still remaining blocks to get the vector of original data blocks 
			/// (optionally also parity, if specified in BeginRecover)
			/// </summary>
			public GFMatrix matrix;
			public int original_numparityblocks = 0, original_numdatablocks = 0;
			public int original_numblocks { get { return original_numdatablocks + original_numparityblocks; } }


			public GaloisField field { get { return matrix.field; } }
			/// <summary>
			/// 1 or 2 byte for gf8 or gf16.
			/// If using non-8/16 bit galois fields or where the data type has extra bits left over,
			/// the high order bits must be 0 in every buffer passed to every generate/recover function.
			/// </summary>
			public int datatypebytes;

			/// <summary>
			/// key: the block's original index. value: the block's index in the vector of blocks to use for recovery.
			/// </summary>
			public Dictionary<int, int> RecoveryVectorPositions = new Dictionary<int, int>();


			/// <summary>
			/// returns true if the block's intact contents are used for recovery
			/// </summary>
			public bool IsBlockNeededForRecovery(int index)
			{
				return RecoveryVectorPositions.ContainsKey(index);
			}

			/// <summary>
			/// false will only recover data
			/// </summary>
			public bool recoveringparity;
		}



	} //reedsolomon class


}//namespace
