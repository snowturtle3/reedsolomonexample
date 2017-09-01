using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


using ReedSolomonNs;
using System.IO;
using static UtilStatic;
using UtilNs;
using BufferNs;


namespace Example1Ns
{


	/// <summary>
	/// example using the RSBlock classes directly
	/// </summary>
	public class Example1
	{
		public static void PrintBlocks(IList<RSBlock> blocks)
		{
			foreach (var block in blocks)
			{
				if (block.IsZeroFilled())
				{
					Console.WriteLine("RSBlockType.ZeroFilled block");
				}
				else
				{
					Console.WriteLine(Util.FormatHex(block.buffer.AsEnumerable()));
				}
			}

			Console.WriteLine();
		}

		public static void ClearBlock(RSBlock block)
		{
			block.buffer.ZeroFill();
			block.SetTypeFlag(RSBlockType.NeedsGenerating);
		}


		public static void MarkBlocksValid(IList<RSBlock> blocks, int startindex, int numblockstomark)
		{
			for (int i = startindex; i < startindex + numblockstomark; i++)
			{
				blocks[i].ClearTypeFlag(RSBlockType.NeedsGenerating);
			}
		}



		public static void go()
		{
			Func<byte[], int, RSBlockType, RSBlock>
				mb = (data, index, blocktype) => new RSBlock(data == null ? null : new ArrayBackedBuffer(data), index, blocktype);


			var blocks = new RSBlock[]
			{
				mb(new byte[]{1,2,3,10}, 0, RSBlockType.Data),
				mb(new byte[]{4,5,6,11}, 1, RSBlockType.Data),
				mb(new byte[]{7,8,9, 0}, 2, RSBlockType.Data),
				mb(null,                 3, RSBlockType.Data | RSBlockType.ZeroFilled),
				mb(new byte[] {0,0,0,0}, 4, RSBlockType.Parity | RSBlockType.NeedsGenerating),
				mb(new byte[] {0,0,0,0}, 5, RSBlockType.Parity | RSBlockType.NeedsGenerating),
				mb(new byte[] {0,0,0,0}, 6, RSBlockType.Parity | RSBlockType.NeedsGenerating)
			};

			int numdatablocks = 4, numparityblocks = 3;
			var galoisfield = ReedSolomon.gf8;

			Console.WriteLine("initial block state");
			PrintBlocks(blocks);

			Console.WriteLine("creating parity");
			ReedSolomon.GenerateParityBlocks(blocks, galoisfield);
			MarkBlocksValid(blocks, numdatablocks, numparityblocks);
			PrintBlocks(blocks);

			Console.WriteLine("destroying some blocks, both data and parity");
			ClearBlock(blocks[1]);
			ClearBlock(blocks[2]);
			ClearBlock(blocks[4]);
			PrintBlocks(blocks);

			Console.WriteLine("repairing");
			ReedSolomon.RecoverDataBlocks(blocks, numdatablocks, numparityblocks, galoisfield);
			PrintBlocks(blocks);

			Util.ConsolePause();
		}


	}

}

namespace Example2Ns
{ 


	/// <summary>
	/// gets user input from console or files
	/// </summary>
	public class InputSource
	{
		public TextReader input = Console.In;
		public TextWriter output = Console.Out;
		public bool allowretries = true;


		public long PromptLong(string prompt = "")
		{
			output.Write(prompt);
			long value;
			while (true)
			{
				if ( long.TryParse(input.ReadLine(), out value) ) { return value; }
				if (!allowretries) { throw new Exception("not an int"); }
				output.Write("not an int; try again: ");
			}
		}


		/// <summary>
		/// asks the user to select an option. the user can enter either the single character or single word name of the option. 
		/// returns the single word name of the option selected.
		/// </summary>
		/// <param name="betweenoptions">separator text printed between the text of option</param>
		/// <param name="afteroptions">separator text printed after all options</param>
		public string PromptOption(char[] singlechars, string[] singlewords, string[] descriptions, string betweenoptions = "\n", string afteroptions = "\n")
		{
			BAssert(singlechars.Length == singlewords.Length && singlewords.Length == descriptions.Length,
				"descriptions not same length");


			for(int i = 0; i < singlechars.Length; i++)
			{
				if (i != 0) { output.Write(betweenoptions); }
				output.Write($"[{singlechars[i]}] {singlewords[i]}");
				if(descriptions[i] != null && descriptions[i].Length != 0)
					{ output.Write($": {descriptions[i]}"); }
			}
			output.Write(afteroptions);


			var singlechars_lower = singlechars.Select(c => char.ToLower(c) ).ToList();
			var singlewords_lower = singlewords.Select(s => s.ToLower() ).ToList();

			output.Write("choose option: ");

			while (true)
			{
				int i;
				string s = input.ReadLine();
				
				if (-1 != (i = singlewords_lower.IndexOf(s)))
					{ return singlewords[i]; }
				if ( s.Length == 1 && -1 != (i = singlechars_lower.IndexOf(char.Parse(s))))
					{ return singlewords[i]; }
				if (!allowretries)
					{ throw new Exception($"unknown option: {s}"); }
				output.Write($"unknown option: {s}. try again: ");
			}
				
		}


		public string PromptFilename(string prompt, bool mustexist)
		{
			output.Write(prompt);

			while (true)
			{
				string filename = input.ReadLine().Replace("\"","");
				if (!mustexist || File.Exists(filename)) { return filename; }
				if (!allowretries) { throw new Exception("file does not exist"); }
				output.Write("file does not exist. try again: ");
			}
		}
	}



	/// <summary>
	/// the data the user is expected to input, to describe the set of files to be processed
	/// </summary>
	public class Example2InputData
	{
		int numdatafiles, numparityfiles;
		IList<string> datafilepaths = new List<string>();
		IList<long> datafilelengths = new List<long>();
		string paritybasename;

		public int buffersize = 1024 * 1024;
		public int maxfiles = 65536;

		/// <summary>
		/// operation: true for create, false for recovery
		/// </summary>
		public bool iscreating;
		

		/// <summary>
		/// prompts the user for all input related to the set of files to be processed and the action to be performed.
		/// </summary>
		public Example2InputData(InputSource io, bool iscreating)
		{
			this.iscreating = iscreating;

			numdatafiles = checked((int) io.PromptLong("number of data files: "));
			if(numdatafiles > maxfiles) { throw new Exception($"too many files, max is {maxfiles}"); }

			//this will be sent to null if input was redirected from a file
			if (!iscreating) { io.output.WriteLine(
				"You probably should have chosen the \"save to file\" option during creation to avoid manually entering file sizes."); }

			for (int i = 0; i < numdatafiles; i++)
			{
				string path = io.PromptFilename($"file #{i + 1} path: ", iscreating);
				datafilepaths.Add(path);
				if (iscreating) { datafilelengths.Add(FileUtil.GetLength(path)); }
				else { datafilelengths.Add(io.PromptLong($"file #{i + 1} original size: ")); }
			}
			numparityfiles = checked((int) io.PromptLong("number of parity files: "));
			paritybasename = io.PromptFilename("base path (will have numbers added) for parity files: ", false);

			if(checked (numdatafiles + numparityfiles) > maxfiles)
			{
				throw new Exception($"too many files, max is {maxfiles}");
			}
			
		}
		

		public Example2InputData(IEnumerable<string> datapaths, string paritybase, int numdatafiles, int numparityfiles, bool iscreating, 
			IEnumerable<long> datafilelengths = null)
		{
			this.datafilepaths = datapaths.ToList();
			this.paritybasename = paritybase;
			this.numdatafiles = numdatafiles;
			this.numparityfiles = numparityfiles;
			this.iscreating = iscreating;
			if (iscreating && datafilelengths == null)
			{
				foreach(var path in this.datafilepaths)
				{
					this.datafilelengths.Add(path.IsNullOrEmpty() ? 0 : FileUtil.GetLength(path));
				}
			}
			else
			{
				this.datafilelengths = datafilelengths.ToList();
			}
		}



		/// <summary>
		/// saves, to a file, the input that the user would need to enter to do the recovery operation
		/// </summary>
		public void ToFile(TextWriter output)
		{
			output.WriteLine(numdatafiles);
			for (int i = 0; i < numdatafiles; i++)
			{
				output.WriteLine(datafilepaths[i]);
				output.WriteLine(datafilelengths[i]);
			}
			output.WriteLine(numparityfiles);
			output.WriteLine(paritybasename);
		}


		public void ToFile(InputSource output)
		{
			ToFile(output.output);
		}

		public void ToFile(string filename)
		{
			using (var textwriter = new StreamWriter(File.OpenWrite(filename)))
			{
				ToFile(textwriter);
			}
		}


		/// <summary>
		/// creates the objects that the user's input describes
		/// </summary>
		public SimpleRsStreamManager CreateFileManager()
		{
			var parityfilenames = new FunctionalList<string>(
				numparityfiles,
				(index) => Path.Combine(Path.GetDirectoryName(paritybasename),
					Path.GetFileNameWithoutExtension(paritybasename) + index.ToString("d5") + Path.GetExtension(paritybasename)
				));

			var filepaths = datafilepaths.Concat(parityfilenames).ToList();

			long parityfilelength = datafilelengths.Max();

			var filelengths = datafilelengths.Concat(Enumerable.Repeat(parityfilelength, numparityfiles)).ToList();

			return new SimpleRsStreamManager(filepaths, numdatafiles, numparityfiles, filelengths, buffersize);
		}
	}

	


	/// <summary>
	/// Creates one stream (data or parity) per file. Deleting any number of files (up to the number of parity files) can be recovered.
	/// Assumes that a file is valid/intact or not based on whether or not it exists,
	/// so changing a file's contents will render the others unrecoverable.
	/// Treats a file as being zero filled if its filename is null or empty.
	/// Treats files as if they were all zero-extended to the length of the longest, for parity calculation.
	/// Obviously this produces parity files that are uselessly &amp; unnecessarily large (if one data file is larger than others),
	/// but is an example of how to use the library.
	/// </summary>
	public class SimpleRsStreamManager: IDisposable
	{

		long buffersize;
		int numdatafiles, numparityfiles;
		IList<string> filepaths;
		IList<long> originalfilelengths;
		RsStreamManager streammanager;

		/// <summary>
		/// true if there are enough files intact for recovery
		/// </summary>
		public bool recoverable { get; private set; } = true;

		/// <summary>
		/// throw this if an impossible recovery is attempted
		/// </summary>
		Exception unrecoverableexception;


		/// <summary>
		/// null path indicates zero filled.
		/// assumes that if a file exists, its data/contents is valid. will recreate any missing files.
		/// the first n=numdatapaths paths are data files, the rest are parity.
		/// </summary>
		public SimpleRsStreamManager(IEnumerable<string> filepaths, int numdatafiles, int numparityfiles, IList<long> originalfilelengths, long buffersize)
		{
			this.numdatafiles = numdatafiles;
			this.numparityfiles = numparityfiles;
			this.buffersize = buffersize;

			this.filepaths = filepaths.ToList();
			this.originalfilelengths = originalfilelengths;

			if (this.filepaths.Count != numdatafiles + numparityfiles)
			{
				throw new ArgumentException($"number of filepaths not equal to {nameof(numdatafiles)} + {nameof(numparityfiles)}");
			}

			streammanager = Create();

		}


		private RsStreamManager Create()
		{

			IList<RSBlock> blocks = new List<RSBlock>();
			IList<Stream> streams = new List<Stream>();


			long numblocksperstream = (originalfilelengths.Max() + (buffersize - 1)) / buffersize;
			long paddedfilesize = numblocksperstream * buffersize;

			
			int blockindex = -1;
			
			foreach (var path in filepaths)
			{
				blockindex++;
				var blocktype = blockindex < numdatafiles ? RSBlockType.Data : RSBlockType.Parity;

				if (path.IsNullOrEmpty())
				{
					blocks.Add( new RSBlock( new ZeroLengthBuffer(), blockindex, blocktype | RSBlockType.ZeroFilled));
					continue;
				}
				else if (!File.Exists(path))
				{
					blocktype |= RSBlockType.NeedsGenerating;
				}

				blocks.Add( new RSBlock( new ArrayBackedBuffer(buffersize), blockindex, blocktype));

			}
			blockindex++;

			if (blockindex != numdatafiles + numparityfiles)
				{ throw new ArgumentException($"number of filepaths not equal to {nameof(numdatafiles)} + {nameof(numparityfiles)}"); }


			int numfilesintact = blocks.Where(block => block.IsIntact()).Count();
			if (numfilesintact < numdatafiles)
			{
				unrecoverableexception = new RsNotEnoughBlocksException(numfilesintact, numdatafiles);
				recoverable = false;
				return null;
			}


			for(int i = 0; i < blocks.Count; i++)
			{
				if (blocks[i].IsProcessingNeeded())
				{
					FileUtil.Create(filepaths[i], originalfilelengths[i]);
				}

				streams.Add( filepaths[i].IsNullOrEmpty() ? null :
					FileUtil.Open(filepaths[i]).ZeroPad(paddedfilesize) );
			}
			

			return new RsStreamManager(blocks, streams, numdatafiles, numparityfiles, numblocksperstream);
		}


		public void GenerateParity()
		{
			if (!recoverable) { throw unrecoverableexception; }
			streammanager.GenerateParity();
		}

		public void Recover()
		{
			if (!recoverable) { throw unrecoverableexception; }
			streammanager.Recover();
		}

		public void Dispose()
		{
			streammanager.Dispose();
		}


	}



	public class Example2
	{

		public static void PrintHelp(TextWriter output)
		{
			string msg =
				"Example program for using this Reed-Solomon library\n" +
				"\n" +
				"To use this example:\n" +
				"* enter a list of files to protect\n" +
				"* generate a number of parity files\n" +
				"* delete some files, then run the program again to recover\n" +
				"As long as the number of files deleted was less than or equal to\n" +
				"the original number of parity files, the deleted files can be recovered.\n" +
				"Assumes the files that still exist are unchanged since parity creation -\n" +
				"changing them will introduce garbage into the files being recovered.\n" +
				"Parity files will be the size of the largest file, \n" +
				"making this example not very practical for actual use.\n" +
				"Also, I suggest using ~500 or less files due to the current\n" +
				"lack of optimization.\n";

			output.Write(msg);
		}



		/// <summary>
		/// determines whether this is a create or repair operation, and gets the appropriate input source (console or file)
		/// </summary>
		/// <param name="creating">outputs true for create operation, false for recover</param>
		/// <returns></returns>
		public static InputSource GetInputSourceAndCommand(out bool creating)
		{

			InputSource io = new InputSource();
			string command;

			command = io.PromptOption(
				new[] { 'c', 'r', 'h' },
				new[] { "create", "recover", "help" },
				new string[] { null, null, null }
				);


			switch (command)
			{
				case "create":
					creating = true;
					return io;

				case "recover":
					creating = false;
					io.input = new StringReader( File.ReadAllText( io.PromptFilename( "path of file containing recovery commands: ", true)));
					io.output = TextWriter.Null;
					io.allowretries = false;
					return io;

				case "help":
					PrintHelp(io.output);
					Util.ConsolePause();
					Environment.Exit(0);
					throw new UnreachableException();

				default:
					throw new Exception($"{nameof(io.PromptOption)} error");
			}
			

		}

		
		
		public static void go()
		{
			try
			{

				bool creating;
				var io = GetInputSourceAndCommand(out creating);
				var datainputted = new Example2InputData(io, creating);

				using (var streammanager = datainputted.CreateFileManager())
				{

					if (datainputted.iscreating)
					{
						datainputted.ToFile(io.PromptFilename("path of file to save recovery commands to: ", false));
						streammanager.GenerateParity();
					}
					else
					{
						streammanager.Recover();
					}

				}
			}
			catch (RsNotEnoughBlocksException e)
			{
				Console.WriteLine($"not enough files to recover: have {e.numblockshave} of {e.numblocksneed} needed");
				Util.ConsolePause();
				Environment.Exit(-1);
			}
			catch (Exception e)
			{
				Console.WriteLine($"error: {e}");
				Util.ConsolePause();
				Environment.Exit(-1);
			}

		}
	}


	public class Example2Test
	{
		string[] datapaths = new[]
		{
			@"c:/test/rs/d1",
			@"c:/test/rs/d2",
			@"c:/test/rs/d3",
			@"c:/test/rs/d4"
		};

		string paritybasepath = @"c:/test/rs/p";
		int numparityfiles = 3;


		byte[][] data = new[]
		{
			new byte[] {1,2,3,4},
			new byte[] {0x11,0x12,0x13,0x14,0x15},
			"testing".ToUtf8Bytes(),
			"testing.........2".ToUtf8Bytes()
		};


		IList<long> datalengths;

		public void initialwrite()
		{
			Directory.CreateDirectory( Path.GetDirectoryName( datapaths[0]));
			for (int i = 0; i < data.Length; i++)
			{
				File.WriteAllBytes(datapaths[i], data[i]);
			}
		}


		public void test()
		{
			initialwrite();
			datalengths = Util.MapList(data, b => b.LongLength);
			bool create = true;
			var input = new Example2Ns.Example2InputData(datapaths, paritybasepath, datapaths.Length, numparityfiles, create, datalengths);
			using (var m = input.CreateFileManager())
			{
				m.GenerateParity();
			}
			Util.ConsolePause("delete some files and press enter to recover");
			input.iscreating = false;

			try
			{
				using (var m = input.CreateFileManager())
				{
					m.Recover();
				}
			}
			catch (RsNotEnoughBlocksException e)
			{
				Console.WriteLine($"not enough files to recover: have {e.numblockshave} of {e.numblocksneed} needed");
			}

		}
	}

}









public class Program
{
	static void Main(string[] args)
	{
		// new Example2Ns.Example2Test().test();

		Example2Ns.Example2.go();
		Console.Write("done. ");
		Util.ConsolePause();
	}

}

