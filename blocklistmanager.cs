using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static UtilStatic;
using ReedSolomonNs;
using UtilNs;
using BufferNs;
using Stream = System.IO.Stream;






namespace ReedSolomonNs
{
	
	//todo desc
	/// <summary>
	/// does reed-solomon processing on streams instead of blocks.
	/// eg, instead of having data and parity blocks you have data and parity streams,
	/// by breaking up each stream into stripes of blocks to be processed separately.
	/// assumes streams are equal length and a multiple of the block buffer size.
	/// streams can be null if the corresponding blocks are marked as zerofilled.
	/// </summary>
	public class RsStreamManager : IDisposable
	{

		IList<RSBlock> blocks;
		IList<Stream> streams;
		IList<bool> areblocksintact;
		GaloisField galoisfield = ReedSolomon.gf16;
		int numdatablocks, numparityblocks;

		long numblocksperstream;



		/// <summary>
		/// Given an IList&lt;RSBlock&gt;, creates an IList&lt;bool&gt; interface that gets/sets whether the corresponding block has the given flag.
		/// </summary>
		/// <param name="invert">if true, the bools in the list will be the opposite of the flag's state.</param>
		public static IList<bool> CreateFlagWrapper(IList<RSBlock> blocks, RSBlockType flag, bool invert = false)
		{
			return Util.MapList(blocks,
				(block) => (block.HasTypeFlag(flag) ^ invert),
				(block, flagstatus) => { block.SetTypeFlag(flag, flagstatus ^ invert); }
				);
		}


		/// <summary>
		/// 
		/// </summary>
		/// <param name="streams">streams for zerofilled blocks can be null</param>
		/// <param name="numblocksperstream">assumes that the stream length is a multiple of the block size</param>
		public RsStreamManager(IList<RSBlock> blocks, IList<Stream> streams, int numdatablocks, int numparityblocks, long numblocksperstream)
		{
			this.blocks = blocks;
			this.streams = streams;
			areblocksintact = CreateFlagWrapper(blocks, RSBlockType.NeedsGenerating, true);
			this.numdatablocks = numdatablocks;
			this.numparityblocks = numparityblocks;
			this.numblocksperstream = numblocksperstream;
		}

		/// <summary>
		/// read all intact blocks and zero the blocks to be calculated
		/// </summary>
		void AdvancePre(IList<bool> needsreading, IList<bool> needswriting)
		{
			foreach (var block in blocks)
			{
				if (needsreading[block.index]) { streams[block.index].CopyTo(block.buffer); }
				if (needswriting[block.index]) { block.buffer.ZeroFill(); }
				BAssert(
					!(needsreading[block.index] && needswriting[block.index]),
					"block marked as both needsreading and needswriting"
					);

			}
		}

		/// <summary>
		/// write all changed blocks
		/// </summary>
		void AdvancePost(IList<bool> needswriting)
		{
			foreach (var block in blocks)
			{
				if (needswriting[block.index])
				{
					block.buffer.CopyTo(streams[block.index]);
				}
			}
		}

		/// <summary>
		/// streams need to be seeked back to the beginning before further use
		/// </summary>
		bool doStreamsNeedReset = false;

		/// <summary>
		/// (re)creates parity streams that are marked as damaged. requires all data blocks to be intact
		/// </summary>
		public void GenerateParity()
		{
			if (disposed) { throw new ObjectDisposedException(nameof(RsStreamManager)); }
			if (doStreamsNeedReset)
			{
				foreach (var stream in streams) { stream.Position = 0; }
			}
			doStreamsNeedReset = true;



			var resumeinfo = ReedSolomon.BeginGenerateParityBlocksPartial(numdatablocks, numparityblocks, galoisfield);

			string errormsg = "all data blocks must be intact when generating parity";

			var needsreading = Util.MapList(blocks,
				(block) => !block.IsZeroFilled() && block.IsDataBlock() && BAssert(block.IsIntact(), errormsg)
				);

			var needswriting = Util.MapList(blocks,
				(block) => block.IsProcessingNeeded() && BAssert(block.IsParityBlock(), errormsg)
				);

			for (long i = 0; i < numblocksperstream; i++)
			{
				AdvancePre(needsreading, needswriting);
				ReedSolomon.GenerateParityBlocksPartial(blocks, resumeinfo);
				AdvancePost(needswriting);
			}
		}

		/// <summary>
		/// repairs data streams that are marked as damaged.
		/// </summary>
		public void Recover()
		{
			if (disposed) { throw new ObjectDisposedException(nameof(RsStreamManager)); }
			if (doStreamsNeedReset)
			{
				foreach (var stream in streams) { stream.Position = 0; }
			}
			doStreamsNeedReset = true;



			var resumeinfo = ReedSolomon.BeginRecoverDataBlocksPartial(areblocksintact, numdatablocks, numparityblocks, galoisfield );


			var needsreading = Util.MapList(blocks,
				(block) => !block.IsZeroFilled() && resumeinfo.IsBlockNeededForRecovery(block.index)
				);

			var needswriting = Util.MapList(blocks,
				(block) => block.IsProcessingNeeded()
				);

			for (long i = 0; i < numblocksperstream; i++)
			{
				AdvancePre(needsreading, needswriting);
				ReedSolomon.RecoverDataBlocksPartial(blocks, resumeinfo);
				AdvancePost(needswriting);
			}
		}


		bool disposed = false;

		protected virtual void Dispose(bool disposing)
		{
			if (disposed) { return; }
			if (disposing)
			{
				foreach (var stream in streams) { stream?.Dispose(); }
				streams = null;
			}
			disposed = true;

		}

		/// <summary>
		/// closes all streams
		/// </summary>
		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		~RsStreamManager()
		{
			Dispose(false);
		}

	}


}

