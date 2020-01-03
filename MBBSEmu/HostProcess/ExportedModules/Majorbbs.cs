﻿using MBBSEmu.Btrieve;
using MBBSEmu.CPU;
using MBBSEmu.Logging;
using MBBSEmu.Memory;
using MBBSEmu.Module;
using MBBSEmu.Session;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using Iced.Intel;

namespace MBBSEmu.HostProcess.ExportedModules
{
    /// <summary>
    ///     Class which defines functions that are part of the MajorBBS/WG SDK and included in
    ///     MAJORBBS.H.
    ///
    ///     While a majority of these functions are specific to MajorBBS/WG, some are just proxies for
    ///     Borland C++ macros and are noted as such.
    /// </summary>
    public class Majorbbs : ExportedModuleBase, IExportedModule
    {
        private readonly PointerDictionary<McvFile> _mcvFiles = new PointerDictionary<McvFile>();
        private McvFile _currentMcvFile;
        private McvFile _previousMcvFile;

        private readonly PointerDictionary<BtrieveFile> _btrieveFiles = new PointerDictionary<BtrieveFile>();
        private BtrieveFile _currentBtrieveFile;
        private BtrieveFile _previousBtrieveFile;

        private readonly Dictionary<byte[], IntPtr16> _textVariables = new Dictionary<byte[], IntPtr16>();

        /// <summary>
        ///     Buffer of Data that is stored to be sent to the user
        /// </summary>
        private readonly MemoryStream _outputBuffer;

        public Majorbbs(MbbsModule module, PointerDictionary<UserSession> channelDictionary) : base(module, channelDictionary)
        {
            _outputBuffer = new MemoryStream();
        }

        /// <summary>
        ///     Sets State for the Module Registers and the current Channel Number that's running
        /// </summary>
        /// <param name="registers"></param>
        /// <param name="channelNumber"></param>
        public void SetState(CpuRegisters registers, ushort channelNumber)
        {
            Registers = registers;
            Module.Memory.SetWord((ushort)EnumHostSegments.UserNum, 0, channelNumber);
        }

        /// <summary>
        ///     Invokes method by the specified ordinal
        /// </summary>
        /// <param name="ordinal"></param>
        public ushort Invoke(ushort ordinal)
        {
            return ordinal switch
            {
                561 => srand(),
                599 => time(),
                68 => alczer(),
                331 => gmdnam(),
                574 => strcpy(),
                589 => stzcpy(),
                492 => register_module(),
                456 => opnmsg(),
                441 => numopt(),
                650 => ynopt(),
                389 => lngopt(),
                566 => stgopt(),
                316 => getasc(),
                377 => l2as(),
                77 => atol(),
                601 => today(),
                665 => f_scopy(),
                520 => sameas(),
                628 => usernum(),
                713 => uacoff(),
                474 => prf(),
                463 => outprf(),
                160 => dedcrd(),
                629 => usrptr(),
                476 => prfmsg(),
                550 => shocst(),
                97 => channel(),
                59 => addcrd(),
                366 => itoa(),
                334 => haskey(),
                335 => hasmkey(),
                486 => rand(),
                428 => ncdate(),
                167 => dfsthn(),
                119 => clsmsg(),
                515 => rtihdlr(),
                516 => rtkick(),
                543 => setmbk(),
                510 => rstmbk(),
                455 => opnbtv(),
                534 => setbtv(),
                569 => stpbtv(),
                505 => rstbtv(),
                621 => updbtv(),
                351 => insbtv(),
                565 => status(),
                488 => rdedcrd(),
                559 => spr(),
                659 => f_lxmul(),
                654 => f_ldiv(),
                113 => clrprf(),
                65 => alcmem(),
                643 => vsprintf(),
                1189 => scnmdf(),
                435 => now(),
                657 => f_lumod(),
                544 => setmem(),
                475 => prfbuf(),
                582 => strncpy(),
                494 => register_textvar(),
                997 => obtbtvl(),
                158 => dclvda(),
                437 => nterms(),
                _ => throw new ArgumentOutOfRangeException($"Unknown Exported Function Ordinal: {ordinal}")
            };
        }

        /// <summary>
        ///     Initializes the Pseudo-Random Number Generator with the given seen
        ///
        ///     Since we'll handle this internally, we'll just ignore this
        ///
        ///     Signature: void srand (unsigned int seed);
        /// </summary>
        public ushort srand() => 0;

        /// <summary>
        ///     Get the current calendar time as a value of type time_t
        ///     Epoch Time
        /// 
        ///     Signature: time_t time (time_t* timer);
        ///     Return: Value is 32-Bit TIME_T (AX:DX)
        /// </summary>
        public ushort time()
        {
            //For now, ignore the input pointer for time_t

            var outputArray = new byte[4];
            var passedSeconds = (int)(DateTime.Now - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            Array.Copy(BitConverter.GetBytes(passedSeconds), 0, outputArray, 0, 4);

           Registers.AX = BitConverter.ToUInt16(outputArray, 2);
           Registers.DX = BitConverter.ToUInt16(outputArray, 0);

#if DEBUG
            _logger.Info($"Passed seconds: {passedSeconds} (AX:{Registers.AX:X4}, DX:{Registers.DX:X4})");
#endif

            return 0;
        }

        /// <summary>
        ///     Allocate a new memory block and zeros it out on the host
        /// 
        ///     Signature: char *alczer(unsigned nbytes);
        ///     Return: AX = Offset in Segment (host)
        ///             DX = Data Segment
        /// </summary>
        public ushort alczer()
        {
            var size = GetParameter(0);

            //Get the current pointer
            var pointer = Module.Memory.AllocateHostMemory(size);

            Registers.AX = pointer;
            Registers.DX = (ushort)EnumHostSegments.HostMemorySegment;

#if DEBUG
            _logger.Info($"Allocated {size} bytes starting at {pointer:X4}");
#endif

            return 0;
        }

        /// <summary>
        ///     Allocates Memory
        ///
        ///     Functionally, for our purposes, the same as alczer
        /// </summary>
        /// <returns></returns>
        public ushort alcmem() => alczer();

        /// <summary>
        ///     Get's a module's name from the specified .MDF file
        /// 
        ///     Signature: char *gmdnam(char *mdfnam);
        ///     Return: AX = Offset in Segment
        ///             DX = Data Segment
        /// </summary>
        public ushort gmdnam()
        {
            var datSegmentOffset = GetParameter(0);
            var dataSegment = GetParameter(1);
            var size = GetParameter(2);

            //Only needs to be set once
            if (!HostMemoryVariables.TryGetValue("GMDNAM", out var variablePointer))
            {

                //Get the current pointer
                var offset = Module.Memory.AllocateHostMemory(size);

                variablePointer = new IntPtr16(HostMemorySegment, offset);

                //Get the Module Name from the Mdf
                var moduleName = Module.Mdf.ModuleName;

                //Sanity Check -- 
                if (moduleName.Length > size)
                {
                    _logger.Warn($"Module Name \"{moduleName}\" greater than specified size {size}, truncating");
                    moduleName = moduleName.Substring(0, size);
                }

                //Set Dictionary Lookup
                HostMemoryVariables["GMDNAM"] = variablePointer;


                //Set Memory
                Module.Memory.SetArray(variablePointer.Segment, variablePointer.Offset,
                    Encoding.Default.GetBytes(Module.Mdf.ModuleName));
#if DEBUG
                _logger.Info(
                    $"Retrieved Module Name \"{Module.Mdf.ModuleName}\" and saved it at host memory offset {Registers.DX:X4}:{Registers.AX:X4}");
#endif
            }

            Registers.AX = variablePointer.Offset;
            Registers.DX = variablePointer.Segment;

            return 0;
        }

        /// <summary>
        ///     Copies the C string pointed by source into the array pointed by destination, including the terminating null character
        ///
        ///     Signature: char* strcpy(char* destination, const char* source );
        ///     Return: AX = Offset in Segment
        ///             DX = Data Segment
        /// </summary>
        public ushort strcpy()
        {
            var destinationOffset = GetParameter(0);
            var destinationSegment = GetParameter(1);
            var srcOffset = GetParameter(2);
            var srcSegment = GetParameter(3);

            var inputBuffer = Module.Memory.GetString(srcSegment, srcOffset);
            Module.Memory.SetArray(destinationSegment, destinationOffset, inputBuffer);

#if DEBUG
            _logger.Info($"Copied {inputBuffer.Length} bytes from {srcSegment:X4}:{srcOffset:X4} to {destinationSegment:X4}:{destinationOffset:X4}");
#endif

            Registers.AX = destinationOffset;
            Registers.DX = destinationSegment;

            return 0;
        }

        /// <summary>
        ///     Copies a string with a fixed length
        ///
        ///     Signature: stzcpy(char *dest, char *source, int nbytes);
        ///     Return: AX = Offset in Segment
        ///             DX = Data Segment
        /// </summary>
        public ushort stzcpy()
        {
            var destinationOffset = GetParameter(0);
            var destinationSegment = GetParameter(1);
            var srcOffset = GetParameter(2);
            var srcSegment = GetParameter(3);
            var limit = GetParameter(4);

            using var inputBuffer = new MemoryStream();
            inputBuffer.Write(Module.Memory.GetSpan(srcSegment, srcOffset, limit));

            //If the value read is less than the limit, it'll be padded with null characters
            //per the MajorBBS Development Guide
            for (var i = inputBuffer.Length; i < limit; i++)
                inputBuffer.WriteByte(0x0);

            Module.Memory.SetArray(destinationSegment, destinationOffset, inputBuffer.ToArray());

#if DEBUG
            _logger.Info($"Copied {inputBuffer.Length} bytes from {srcSegment:X4}:{srcOffset:X4} to {destinationSegment:X4}:{destinationOffset:X4}");
#endif
            Registers.AX = destinationOffset;
            Registers.DX = destinationSegment;

            return 0;
        }

        /// <summary>
        ///     Registers the Module with the MajorBBS system
        ///
        ///     Signature: int register_module(struct module *mod)
        ///     Return: AX = Value of usrptr->state whenever user is 'in' this module
        /// </summary>
        public ushort register_module()
        {
            var destinationOffset = GetParameter(0);
            var destinationSegment = GetParameter(1);

            var moduleStruct = Module.Memory.GetArray(destinationSegment, destinationOffset, 61);

            var relocationRecords =
                Module.File.SegmentTable.First(x => x.Ordinal == destinationSegment).RelocationRecords;

            //Description for Main Menu
            var moduleDescription = Encoding.Default.GetString(moduleStruct, 0, 25);
            Module.ModuleDescription = moduleDescription;
#if DEBUG
            _logger.Info($"Module Description set to {moduleDescription}");
#endif

            var moduleRoutines = new[]
                {"lonrou", "sttrou", "stsrou", "injrou", "lofrou", "huprou", "mcurou", "dlarou", "finrou"};

            for (var i = 0; i < 9; i++)
            {
                var currentOffset = (ushort) (25 + (i * 4));
                var routineEntryPoint = new byte[4];
                Array.Copy(moduleStruct, currentOffset, routineEntryPoint, 0, 4);

                //If there's a Relocation record for this routine, apply it
                if(relocationRecords.TryGetValue(currentOffset, out var routineRelocationRecord))
                {
                    Array.Copy(BitConverter.GetBytes(routineRelocationRecord.TargetTypeValueTuple.Item4), 0,
                        routineEntryPoint, 0, 2);
                    Array.Copy(BitConverter.GetBytes(routineRelocationRecord.TargetTypeValueTuple.Item2), 0,
                        routineEntryPoint, 2, 2);
                }

                //Setup the Entry Points in the Module
                Module.EntryPoints[moduleRoutines[i]] = new IntPtr16(BitConverter.ToUInt16(routineEntryPoint, 2), BitConverter.ToUInt16(routineEntryPoint, 0));

#if DEBUG
                _logger.Info(
                    $"Routine {moduleRoutines[i]} set to {BitConverter.ToUInt16(routineEntryPoint, 2):X4}:{BitConverter.ToUInt16(routineEntryPoint, 0):X4}");
#endif
            }

            //usrptr->state is the Module Number in use, as assigned by the host process
            //Because we only support 1 module running at a time right now, we just set this to one
            Registers.AX = 1;

            return 0;
        }

        /// <summary>
        ///     Opens the specified CNF file (.MCV in runtime form)
        ///
        ///     Signature: FILE *mbkprt=opnmsg(char *fileName)
        ///     Return: AX = Offset in Segment
        ///             DX = Host Segment
        /// </summary>
        public ushort opnmsg()
        {
            var sourceOffset = GetParameter(0);
            var sourceSegment = GetParameter(1);

            var msgFileName = Encoding.Default.GetString(Module.Memory.GetString(sourceSegment, sourceOffset));

            msgFileName = msgFileName.TrimEnd('\0');

            _currentMcvFile = new McvFile(msgFileName, Module.ModulePath);

            var offset = _mcvFiles.Allocate(_currentMcvFile);

#if DEBUG
            _logger.Info(
                $"Opened MSG file: {msgFileName}, assigned to {(int) EnumHostSegments.Msg:X4}:{offset:X4}");
#endif
            Registers.AX = (ushort)offset;
            Registers.DX = (ushort)EnumHostSegments.Msg;

            return 0;
        }

        /// <summary>
        ///     Retrieves a numeric option from MCV file
        ///
        ///     Signature: int numopt(int msgnum,int floor,int ceiling)
        ///     Return: AX = Value retrieved
        /// </summary>
        public ushort numopt()
        {
            if(_mcvFiles.Count == 0)
                throw new Exception("Attempted to read configuration value from MSG file prior to calling opnmsg()");

            var msgnum = GetParameter(0);
            var floor = (short)GetParameter(1);
            var ceiling = GetParameter(2);

            var outputValue = _currentMcvFile.GetNumeric(msgnum);

            //Validate
            if(outputValue < floor || outputValue >  ceiling)
                throw new ArgumentOutOfRangeException($"{msgnum} value {outputValue} is outside specified bounds");

#if DEBUG
            _logger.Info($"Retrieved option {msgnum} value: {outputValue}");
#endif

            Registers.AX = (ushort) outputValue;

            return 0;
        }

        /// <summary>
        ///     Retrieves a yes/no option from an MCV file
        ///
        ///     Signature: int ynopt(int msgnum)
        ///     Return: AX = 1/Yes, 0/No
        /// </summary>
        public ushort ynopt()
        {
            var msgnum = GetParameter(0);

            var outputValue = _currentMcvFile.GetBool(msgnum);

#if DEBUG
            _logger.Info($"Retrieved option {msgnum} value: {outputValue}");
#endif

            Registers.AX = (ushort)(outputValue ? 1 : 0);

            return 0;
        }

        /// <summary>
        ///     Gets a long (32-bit) numeric option from the MCV File
        ///
        ///     Signature: long lngopt(int msgnum,long floor,long ceiling)
        ///     Return: AX = Most Significant 16-Bits
        ///             DX = Least Significant 16-Bits
        /// </summary>
        public ushort lngopt()
        {
            var msgnum = GetParameter(0);

            var floorLow = GetParameter(1);
            var floorHigh = GetParameter(2);

            var ceilingLow = GetParameter(3);
            var ceilingHigh = GetParameter(4);

            var floor = floorHigh << 16 | floorLow;
            var ceiling = ceilingHigh << 16 | ceilingLow;

            var outputValue = _currentMcvFile.GetLong(msgnum);

            //Validate
            if (outputValue < floor || outputValue > ceiling)
                throw new ArgumentOutOfRangeException($"{msgnum} value {outputValue} is outside specified bounds");

#if DEBUG
            _logger.Info($"Retrieved option {msgnum} value: {outputValue}");
#endif

            Registers.DX = (ushort)(outputValue >> 16);
            Registers.AX = (ushort)(outputValue & 0xFFFF);

            return 0;
        }

        /// <summary>
        ///     Gets a string from an MCV file
        ///
        ///     Signature: char *string=stgopt(int msgnum)
        ///     Return: AX = Offset in Segment
        ///             DX = Host Segment     
        /// </summary>
        public ushort stgopt()
        {
            var msgnum = GetParameter(0);

            if(!HostMemoryVariables.TryGetValue($"STGOPT_{msgnum}", out var variablePointer))
            {
                var outputValue = _currentMcvFile.GetString(msgnum);
                var offset = Module.Memory.AllocateHostMemory((ushort)outputValue.Length);

                variablePointer = new IntPtr16((ushort)EnumHostSegments.HostMemorySegment, offset);

                //Save Variable Pointer
                HostMemoryVariables[$"STGOPT_{msgnum}"] = variablePointer;

                //Set Value in Memory
                Module.Memory.SetArray(variablePointer.Segment, variablePointer.Offset, outputValue);

#if DEBUG
                _logger.Info(
                    $"Retrieved option {msgnum} string value: {outputValue.Length} bytes saved to {variablePointer.Segment:X4}:{variablePointer.Offset:X4}");
#endif
            }
            Registers.AX = variablePointer.Offset;
            Registers.DX = variablePointer.Segment;

            return 0;
        }

        /// <summary>
        ///     Read value of CNF option (text blocks with ASCII compatible line terminators)
        ///
        ///     Functionally, as far as this helper method is concerned, there's no difference between this method and stgopt()
        /// 
        ///     Signature: char *bufadr=getasc(int msgnum)
        ///     Return: AX = Offset in Segment
        ///             DX = Host Segment 
        /// </summary>
        public ushort getasc()
        {
#if DEBUG
            _logger.Info($"Called, redirecting to stgopt()");
#endif
            stgopt();

            return 0;
        }

        /// <summary>
        ///     Converts a long to an ASCII string
        ///
        ///     Signature: char *l2as(long longin)
        ///     Return: AX = Offset in Segment
        ///             DX = Host Segment 
        /// </summary>
        public ushort l2as()
        {
            var lowByte = GetParameter(0);
            var highByte = GetParameter(1);

            var outputValue = $"{highByte << 16 | lowByte}\0";

            if (!HostMemoryVariables.TryGetValue($"L2AS", out var variablePointer))
            {
                //Pre-allocate space for the maximum number of characters for a ulong
                var offset = Module.Memory.AllocateHostMemory((ushort)uint.MaxValue.ToString().Length);

                variablePointer = new IntPtr16((ushort) EnumHostSegments.HostMemorySegment, offset);

                //Set Variable Pointer
                HostMemoryVariables["L2AS"] = variablePointer;
            }

            Module.Memory.SetArray(variablePointer.Segment, variablePointer.Offset, Encoding.Default.GetBytes(outputValue));

#if DEBUG
            _logger.Info(
                $"Received value: {outputValue}, string saved to {variablePointer.Segment:X4}:{variablePointer.Offset:X4}");
#endif

            Registers.AX = variablePointer.Offset;
            Registers.DX = variablePointer.Segment;

            return 0;
        }

        /// <summary>
        ///     Converts string to a long integer
        ///
        ///     Signature: long int atol(const char *str)
        ///     Return: AX = Offset in Segment
        ///             DX = Host Segment  
        /// </summary>
        public ushort atol()
        {
            var sourceOffset = GetParameter(0);
            var sourceSegment = GetParameter(1);

            using var inputBuffer = new MemoryStream();
            inputBuffer.Write(Module.Memory.GetString(sourceSegment, sourceOffset));

            var inputValue = Encoding.Default.GetString(inputBuffer.ToArray()).Trim('\0');

            if (!int.TryParse(inputValue, out var outputValue))
            {
                /*
                 * Unsuccessful parsing returns a 0 value
                 * More info: http://www.cplusplus.com/reference/cstdlib/atol/
                 */
#if DEBUG
                _logger.Warn($"atol(): Unable to cast string value located at {sourceSegment:X4}:{sourceOffset:X4} to long");
#endif
                Registers.AX = 0;
                Registers.DX = 0;
                Registers.F.SetFlag(EnumFlags.CF);
                return 0;
            }

#if DEBUG
            _logger.Info($"Cast {inputValue} ({sourceSegment:X4}:{sourceOffset:X4}) to long");
#endif

            Registers.DX = (ushort)(outputValue >> 16);
            Registers.AX = (ushort)(outputValue & 0xFFFF);
            Registers.F.ClearFlag(EnumFlags.CF);
            return 0;
        }

        /// <summary>
        ///     Find out today's date coded as YYYYYYYMMMMDDDDD
        ///
        ///     Signature: int date=today()
        ///     Return: AX = Packed Date
        /// </summary>
        public ushort today()
        { 
            //From DOSFACE.H:
            //#define dddate(mon,day,year) (((mon)<<5)+(day)+(((year)-1980)<<9))
            var packedDate = (DateTime.Now.Month << 5) + DateTime.Now.Day + ((DateTime.Now.Year - 1980) << 9);

#if DEBUG
            _logger.Info($"Returned packed date: {packedDate}");
#endif
            
            Registers.AX = (ushort)packedDate;

            return 0;
        }

        /// <summary>
        ///     Copies Struct into another Struct (Borland C++ Implicit Function)
        ///     CX contains the number of bytes to be copied
        ///
        ///     Signature: None -- Compiler Generated
        ///     Return: None
        /// </summary>
        public ushort f_scopy()
        {
            var srcOffset = GetParameter(0);
            var srcSegment = GetParameter(1);
            var destinationOffset = GetParameter(2);
            var destinationSegment = GetParameter(3);

            using var inputBuffer = new MemoryStream();

            inputBuffer.Write(Module.Memory.GetArray(srcSegment, srcOffset, Registers.CX));

            Module.Memory.SetArray(destinationSegment, destinationOffset, inputBuffer.ToArray());

#if DEBUG
            _logger.Info($"Copied {inputBuffer.Length} bytes from {srcSegment:X4}:{srcOffset:X4} to {destinationSegment:X4}:{destinationOffset:X4}");
#endif
            return 0;
        }

        /// <summary>
        ///     Case ignoring string match
        ///
        ///     Signature: int match=sameas(char *stgl, char* stg2)
        ///     Returns: AX = 1 if match
        /// </summary>
        public ushort sameas()
        {
            var string1Offset = GetParameter(0);
            var string1Segment = GetParameter(1);
            var string2Offset = GetParameter(2);
            var string2Segment = GetParameter(3);

            using var string1InputBuffer = new MemoryStream();
            string1InputBuffer.Write(Module.Memory.GetString(string1Segment, string1Offset));
            var string1InputValue = Encoding.Default.GetString(string1InputBuffer.ToArray()).ToUpper();

            using var string2InputBuffer = new MemoryStream();
            string2InputBuffer.Write(Module.Memory.GetString(string2Segment, string2Offset));
            var string2InputValue = Encoding.Default.GetString(string2InputBuffer.ToArray()).ToUpper();

            var resultValue = string1InputValue == string2InputValue;

#if DEBUG
            _logger.Info($"Returned {resultValue} comparing {string1InputValue} ({string1Segment:X4}:{string1Offset:X4}) to {string2InputValue} ({string2Segment:X4}:{string2Offset:X4})");
#endif

            Registers.AX = (ushort)(resultValue ? 1 : 0);

            return 0;
        }

        /// <summary>
        ///     Property with the User Number (Channel) of the user currently being serviced
        ///
        ///     Signature: int usrnum
        ///     Retrurns: int == User Number (Channel)
        /// </summary>
        /// <returns></returns>
        public ushort usernum() => (ushort)EnumHostSegments.UserNum;

        /// <summary>
        ///     Gets the online user account info
        /// 
        ///     Signature: struct usracc *uaptr=uacoff(unum)
        ///     Return: AX = Offset in Segment
        ///             DX = Host Segment  
        /// </summary>
        /// <returns></returns>
        public ushort uacoff()
        {
            var userNumber = GetParameter(0);

            Module.Memory.SetArray((ushort)EnumHostSegments.UsrAcc, 0, ChannelDictionary[userNumber].UsrAcc.ToSpan());

            Registers.AX = 0;
            Registers.DX = (ushort)EnumHostSegments.UsrAcc;

            return 0;
        }

        /// <summary>
        ///     Like printf(), except the converted text goes into a buffer
        ///
        ///     Signature: void prf(string)
        /// </summary>
        /// <returns></returns>
        public ushort prf()
        {
            var sourceOffset = GetParameter(0);
            var sourceSegment = GetParameter(1);

            var output = Module.Memory.GetString(sourceSegment, sourceOffset);

            //If the supplied string has any control characters for formatting, process them
            var formattedMessage = FormatPrintf(output, 2);

            _outputBuffer.Write(formattedMessage);
            Module.Memory.SetArray((ushort)EnumHostSegments.Prfbuf, 0, _outputBuffer.ToArray());

#if DEBUG
            _logger.Info($"Added {output.Length} bytes to the buffer");
#endif

            return 0;
        }


        /// <summary>
        ///     Resets prf buffer
        /// </summary>
        /// <returns></returns>
        public ushort clrprf()
        {
            _outputBuffer.SetLength(0);

#if DEBUG
            _logger.Info("Reset Output Buffer");
#endif

            return 0;
        }

        /// <summary>
        ///     Send prfbuf to a channel & clear
        ///
        ///     Signature: void outprf (unum)
        /// </summary>
        /// <returns></returns>
        public ushort outprf()
        {
            var userChannel = GetParameter(0);

            ChannelDictionary[userChannel].DataToClient.Enqueue(_outputBuffer.ToArray());
            _outputBuffer.SetLength(0);

            return 0;
        }

        /// <summary>
        ///     Deduct real credits from online acct
        ///
        ///     Signature: int enuf=dedcrd(long amount, int asmuch)
        ///     Returns: AX = 1 == Had enough, 0 == Not enough
        /// </summary>
        /// <returns></returns>
        public ushort dedcrd()
        {
            var sourceOffset = GetParameter(0);
            var lowByte = GetParameter(1);
            var highByte = GetParameter(2);

            var creditsToDeduct = (highByte << 16 | lowByte);

#if DEBUG
            _logger.Info($"Deducted {creditsToDeduct} from the current users account (unlimited)");
#endif

            Registers.AX = 1;
            return 0;
        }

        /// <summary>
        ///     Points to that channels 'user' struct
        ///     This is held in its own data segment on the host
        ///
        ///     Signature: struct user *usrptr;
        ///     Returns: int = Segment on host for User Pointer
        /// </summary>
        /// <returns></returns>
        public ushort usrptr() => (ushort)EnumHostSegments.UserPtr;

        /// <summary>
        ///     Like prf(), but the control string comes from an .MCV file
        ///
        ///     Signature: void prfmsg(msgnum,p1,p2, ..• ,pn);
        /// </summary>
        /// <returns></returns>
        public ushort prfmsg()
        {
            var messageNumber = GetParameter(0);

            if (!_currentMcvFile.Messages.TryGetValue(messageNumber, out var outputMessage))
            {
                _logger.Warn($"prfmsg() unable to locate message number {messageNumber} in current MCV file {_currentMcvFile.FileName}");
                _outputBuffer.WriteByte(0x0);
                return 0;
            }

            var formattedMessage = FormatPrintf(outputMessage, 1);

            _outputBuffer.Write(formattedMessage);
            Module.Memory.SetArray((ushort)EnumHostSegments.Prfbuf, 0, _outputBuffer.ToArray());

#if DEBUG
            _logger.Info($"Added {formattedMessage.Length} bytes to the buffer from message number {messageNumber}");
#endif
            return 0;
        }

        /// <summary>
        ///     Displays a message in the Audit Trail
        ///
        ///     Signature: void shocst(char *summary, char *detail, p1, p1,...,pn);
        /// </summary>
        /// <returns></returns>
        public ushort shocst()
        {
            var string1Offset = GetParameter(0);
            var string1Segment = GetParameter(1);
            var string2Offset = GetParameter(2);
            var string2Segment = GetParameter(3);

            using var string1InputBuffer = new MemoryStream();
            string1InputBuffer.Write(Module.Memory.GetString(string1Segment, string1Offset));
            var string1InputValue = Encoding.Default.GetString(string1InputBuffer.ToArray());

            using var string2InputBuffer = new MemoryStream();
            string2InputBuffer.Write(Module.Memory.GetString(string2Segment, string2Offset));

            //If the supplied string has any control characters for formatting, process them
            var string2InputValue = FormatPrintf(string2InputBuffer.ToArray(), 4);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.BackgroundColor = ConsoleColor.Blue;
            Console.WriteLine($"AUDIT SUMMARY: {string1InputValue}");
            Console.WriteLine($"AUDIT DETAIL: {Encoding.ASCII.GetString(string2InputValue)}");
            Console.ResetColor();

            return 0;
        }

        /// <summary>
        ///     Array of chanel codes (as displayed)
        ///
        ///     Signature: int *channel
        /// </summary>
        /// <returns></returns>
        public ushort channel() => (ushort) EnumHostSegments.ChannelArray;

        /// <summary>
        ///     Post credits to the specified Users Account
        ///
        ///     signature: int addcrd(char *keyuid,char *tckstg,int real)
        /// </summary>
        /// <returns></returns>
        public ushort addcrd()
        {
            var string1Offset = GetParameter(0);
            var string1Segment = GetParameter(1);
            var string2Offset = GetParameter(2);
            var string2Segment = GetParameter(3);
            var real = GetParameter(4);

            using var string1InputBuffer = new MemoryStream();
            string1InputBuffer.Write(Module.Memory.GetString(string1Segment, string1Offset));
            var string1InputValue = Encoding.Default.GetString(string1InputBuffer.ToArray());

            using var string2InputBuffer = new MemoryStream();
            string2InputBuffer.Write(Module.Memory.GetString(string2Segment, string2Offset));
            var string2InputValue = Encoding.Default.GetString(string2InputBuffer.ToArray());

#if DEBUG
            _logger.Info($"Added {string2InputValue} credits to user account {string1InputValue} (unlimited -- this function is ignored)");
#endif

            return 0;
        }

        /// <summary>
        ///     Converts an integer value to a null-terminated string using the specified base and stores the result in the array given by str
        ///
        ///     Signature: char *itoa(int value, char * str, int base)
        /// </summary>
        public ushort itoa()
        {
            var integerValue = GetParameter(0);
            var string1Offset = GetParameter(1);
            var string1Segment = GetParameter(2);
            var baseValue = GetParameter(3);

            var output = Convert.ToString((short)integerValue, baseValue);
            output += "\0";

            Module.Memory.SetArray(string1Segment, string1Offset, Encoding.Default.GetBytes(output));

#if DEBUG
            _logger.Info(
                $"Convterted integer {integerValue} to {output} (base {baseValue}) and saved it to {string1Segment:X4}:{string1Offset:X4}");
#endif
            return 0;
        }

        /// <summary>
        ///     Does the user have the specified key
        /// 
        ///     Signature: int haskey(char *lock)
        ///     Returns: AX = 1 == True
        /// </summary>
        /// <returns></returns>
        public ushort haskey()
        {
            var lockNameOffset = GetParameter(0);
            var lockNameSegment = GetParameter(1);
            var lockNameBytes = Module.Memory.GetString(lockNameSegment, lockNameOffset);
            var lockName = Encoding.ASCII.GetString(lockNameBytes.Slice(0, lockNameBytes.Length - 1));

            Registers.AX = 1;
            
            return 0;
        }

        /// <summary>
        ///     Returns if the user has the key specified in an offline Security and Accounting option
        ///
        ///     Signature: int hasmkey(int msgnum)
        ///     Returns: AX = 1 == True
        /// </summary>
        /// <returns></returns>
        public ushort hasmkey()
        {
            var key = GetParameter(0);
            Registers.AX = 1;

            return 0;
        }

        /// <summary>
        ///     Returns a pseudo-random integral number in the range between 0 and RAND_MAX.
        ///
        ///     Signature: int rand (void)
        ///     Returns: AX = 16-bit Random Number
        /// </summary>
        /// <returns></returns>
        public ushort rand()
        {
            var randomValue = new Random(Guid.NewGuid().GetHashCode()).Next(1, short.MaxValue);

#if DEBUG
            _logger.Info($"Generated random number {randomValue} and saved it to AX");
#endif
            Registers.AX = (ushort)randomValue;

            return 0;
        }

        /// <summary>
        ///     Returns Packed Date as a char* in 'MM/DD/YY' format
        ///
        ///     Signature: char *ascdat=ncdate(int date)
        ///     Return: AX = Offset in Segment
        ///             DX = Host Segment  
        /// </summary>
        /// <returns></returns>
        public ushort ncdate()
        {
            /* From DOSFACE.H:
                #define ddyear(date) ((((date)>>9)&0x007F)+1980)
                #define ddmon(date)   (((date)>>5)&0x000F)
                #define ddday(date)    ((date)    &0x001F)
             */

            var packedDate = GetParameter(0);

            //Pack the Date
            var year = ((packedDate >> 9) & 0x007F) + 1980;
            var month = (packedDate >> 5) & 0x000F;
            var day = packedDate & 0x001F;
            var outputDate = $"{month:D2}/{day:D2}/{year%100}\0";

            if (!HostMemoryVariables.TryGetValue("NCDATE", out var variablePointer))
            {
                var offset = Module.Memory.AllocateHostMemory((ushort)outputDate.Length);

                variablePointer = new IntPtr16((ushort)EnumHostSegments.HostMemorySegment, offset);
                HostMemoryVariables["NCDATE"] = variablePointer;
            }

            Module.Memory.SetArray(variablePointer.Segment, variablePointer.Offset, Encoding.Default.GetBytes(outputDate));

#if DEBUG
            _logger.Info($"Received value: {packedDate}, decoded string {outputDate} saved to {variablePointer.Segment:X4}:{variablePointer.Offset:X4}");
#endif
            Registers.AX = variablePointer.Offset;
            Registers.DX = variablePointer.Segment;
            return 0;
        }

        /// <summary>
        ///     Default Status Handler for status conditions this module is not specifically expecting
        ///
        ///     Ignored for now
        /// 
        ///     Signature: void dfsthn()
        /// </summary>
        /// <returns></returns>
        public ushort dfsthn()
        {
            return 0;
        }

        /// <summary>
        ///     Closes the Specified Message File
        ///
        ///     Signature: void clsmsg(FILE *mbkprt)
        /// </summary>
        /// <returns></returns>
        public ushort clsmsg()
        {
            //We ignore this for now, and we'll just keep it open for the time being

            return 0;
        }

        /// <summary>
        ///     Register a real-time routine that needs to execute more than 1 time per second
        ///
        ///     Routines registered this way are executed at 18hz
        /// 
        ///     Signature: void rtihdlr(void (*rouptr)(void))
        /// </summary>
        public ushort rtihdlr()
        {
            var routinePointerOffset = GetParameter(0);
            var routinePointerSegment = GetParameter(1);

            var routine = new RealTimeRoutine(routinePointerSegment, routinePointerOffset);
            var routineNumber = Module.RtihdlrRoutines.Allocate(routine);
            Module.EntryPoints.Add($"RTIHDLR-{routineNumber}", routine);
#if DEBUG
            _logger.Info($"Registered routine {routinePointerSegment:X4}:{routinePointerOffset:X4}");
#endif
            return 0;
        }

        /// <summary>
        ///     'Kicks Off' the specified routine after the specified delay
        ///
        ///     Signature: void rtkick(int time, void *rouptr())
        /// </summary>
        /// <returns></returns>
        public ushort rtkick()
        {
            var delaySeconds = GetParameter(0);
            var routinePointerOffset = GetParameter(1);
            var routinePointerSegment = GetParameter(2);

            var routine = new RealTimeRoutine(routinePointerSegment, routinePointerOffset, delaySeconds);
            var routineNumber = Module.RtkickRoutines.Allocate(routine);

            Module.EntryPoints.Add($"RTKICK-{routineNumber}", routine);

#if DEBUG
            _logger.Info($"Registered routine {routinePointerSegment:X4}:{routinePointerOffset:X4} to execute every {delaySeconds} seconds");
#endif
            return 0;
        }

        /// <summary>
        ///     Sets 'current' MCV file to the specified pointer
        ///
        ///     Signature: FILE *setmbk(mbkptr)
        /// </summary>
        /// <returns></returns>
        public ushort setmbk()
        {
            var mcvFileOffset = GetParameter(0);
            var mcvFileSegment = GetParameter(1);

            if(mcvFileSegment != (ushort)EnumHostSegments.Msg)
                throw new ArgumentException($"Specified Segment for MCV File {mcvFileSegment} does not match host MCV Segment {(int)EnumHostSegments.Msg}");

            _previousMcvFile = _currentMcvFile;
            _currentMcvFile = _mcvFiles[mcvFileOffset];

#if DEBUG
            //_logger.Info($"Set current MCV File: {_mcvFiles[mcvFileOffset].FileName}");
#endif

            return 0;
        }

        /// <summary>
        ///     Restore previous MCV file block ptr from before last setmbk() call
        /// 
        ///     Signature: void rstmbk()
        /// </summary>
        /// <returns></returns>
        public ushort rstmbk()
        {
            _currentMcvFile = _previousMcvFile;

            return 0;
        }

        /// <summary>
        ///     Opens a Btrieve file for I/O
        ///
        ///     Signature: BTVFILE *bbptr=opnbtv(char *filnae, int reclen)
        ///     Return: AX = Offset to File Pointer
        ///             DX = Host Btrieve Segment  
        /// </summary>
        /// <returns></returns>
        public ushort opnbtv()
        {
            var btrieveFilenameOffset = GetParameter(0);
            var btrieveFilenameSegment = GetParameter(1);
            var recordLength = GetParameter(2);

            using var btrieveFilename = new MemoryStream();
            btrieveFilename.Write(Module.Memory.GetString(btrieveFilenameSegment, btrieveFilenameOffset));

            var fileName = Encoding.Default.GetString(btrieveFilename.ToArray()).TrimEnd('\0');

            var btrieveFile = new BtrieveFile(fileName, Module.ModulePath, recordLength);

            var btrieveFilePointer = _btrieveFiles.Allocate(btrieveFile);

            //Set it as current by default
            _currentBtrieveFile = _btrieveFiles[btrieveFilePointer];

#if DEBUG
            _logger.Info($"Opened file {fileName} and allocated it to {(ushort)EnumHostSegments.BtrieveFile:X4}:{btrieveFilePointer:X4}");
#endif
            Registers.AX = (ushort)btrieveFilePointer;
            Registers.DX = (ushort)EnumHostSegments.BtrieveFile;

            return 0;
        }

        /// <summary>
        ///     Used to set the Btrieve file for all subsequent database functions
        ///
        ///     Signature: void setbtv(BTVFILE *bbprt)
        /// </summary>
        /// <returns></returns>
        public ushort setbtv()
        {
            var btrieveFileOffset = GetParameter(0);
            var btrieveFileSegment = GetParameter(1);

            if(btrieveFileSegment != (int)EnumHostSegments.BtrieveFile)
                throw new InvalidDataException($"Invalid Btrieve File Segment provided. Actual: {btrieveFileSegment:X4} Expecting: {(int)EnumHostSegments.BtrieveFile}");

            if(_currentBtrieveFile != null)
                _previousBtrieveFile = _currentBtrieveFile;

            _currentBtrieveFile = _btrieveFiles[btrieveFileOffset];

#if DEBUG
            _logger.Info($"Setting current Btrieve file to {_currentBtrieveFile.FileName} ({btrieveFileSegment:X4}:{btrieveFileOffset:X4})");
#endif

            return 0;
        }

        /// <summary>
        ///     'Step' based Btrieve operation
        ///
        ///     Signature: int stpbtv (void *recptr, int stpopt)
        ///     Returns: AX = 1 == Record Found, 0 == Database Empty
        /// </summary>
        /// <returns></returns>
        public ushort stpbtv()
        {
            if(_currentBtrieveFile == null)
                throw new FileNotFoundException("Current Btrieve file hasn't been set using SETBTV()");

            var btrieveRecordPointerOffset = GetParameter(0);
            var btrieveRecordPointerSegment = GetParameter(1);

            var stpopt = GetParameter(2);

            ushort resultCode = 0;
            switch (stpopt)
            {
                case (ushort)EnumBtrieveOperationCodes.StepFirst:
                    resultCode = _currentBtrieveFile.StepFirst();
                    break;
                case (ushort)EnumBtrieveOperationCodes.StepNext:
                    resultCode = _currentBtrieveFile.StepNext();
                    break;
                default:
                    throw new InvalidEnumArgumentException($"Unknown Btrieve Operation Code: {stpopt}");
            }

            //Set Memory Values
            if (resultCode > 0)
            {
                //See if the segment lives on the host or in the module
                Module.Memory.SetArray(btrieveRecordPointerSegment, btrieveRecordPointerOffset, _currentBtrieveFile.GetRecord());
            }

            Registers.AX = resultCode;

#if DEBUG
            _logger.Info($"Performed Btrieve Step - Record written to {btrieveRecordPointerSegment:X4}:{btrieveRecordPointerOffset:X4}, AX: {resultCode}");
#endif
            return 0;
        }

        /// <summary>
        ///     Restores the last Btrieve data block for use
        ///
        ///     Signature: void rstbtv (void)
        /// </summary>
        /// <returns></returns>
        public ushort rstbtv()
        {
            if (_previousBtrieveFile == null)
            {
#if DEBUG
                _logger.Info($"Previous Btrieve file == null, ignoring");
#endif
                return 0;
            }

            _currentBtrieveFile = _previousBtrieveFile;

#if DEBUG
            _logger.Info($"Set current Btreieve file to {_previousBtrieveFile.FileName}");
#endif
            return 0;
        }

        /// <summary>
        ///     Update the Btrieve current record
        ///
        ///     Signature: void updbtv(char *recptr)
        /// </summary>
        /// <returns></returns>
        public ushort updbtv()
        {
            var btrieveRecordPointerOffset = GetParameter(0);
            var btrieveRecordPointerSegment = GetParameter(1);

            //See if the segment lives on the host or in the module
            using var btrieveRecord = new MemoryStream();
            btrieveRecord.Write(Module.Memory.GetArray(btrieveRecordPointerSegment, btrieveRecordPointerOffset,
                    _currentBtrieveFile.RecordLength));

            _currentBtrieveFile.Update(btrieveRecord.ToArray());

#if DEBUG
            _logger.Info(
                $"Updated current Btrieve record ({_currentBtrieveFile.CurrentRecordNumber}) with {btrieveRecord.Length} bytes");
#endif

            return 0;
        }

        /// <summary>
        ///     Insert new fixed-length Btrieve record
        /// 
        ///     Signature: void insbtv(char *recptr)
        /// </summary>
        /// <returns></returns>
        public ushort insbtv()
        {
            var btrieveRecordPointerOffset = GetParameter(0);
            var btrieveRecordPointerSegment = GetParameter(1);

            //See if the segment lives on the host or in the module
            using var btrieveRecord = new MemoryStream();
            btrieveRecord.Write(Module.Memory.GetArray(btrieveRecordPointerSegment, btrieveRecordPointerOffset,
                    _currentBtrieveFile.RecordLength));

            _currentBtrieveFile.Insert(btrieveRecord.ToArray());

#if DEBUG
            _logger.Info(
                $"Inserted Btrieve record at {_currentBtrieveFile.CurrentRecordNumber} with {btrieveRecord.Length} bytes");
#endif

            return 0;
        }

        /// <summary>
        ///     Raw status from btusts, where appropriate
        ///     
        ///     Signature: int status
        ///     Returns: Segment holding the Users Status
        /// </summary>
        /// <returns></returns>
        public ushort status() => (ushort) EnumHostSegments.Status;

        /// <summary>
        ///     Deduct real credits from online acct
        ///
        ///     Signature: int enuf=rdedcrd(long amount, int asmuch)
        ///     Returns: Always 1, meaning enough credits
        /// </summary>
        /// <returns></returns>
        public ushort rdedcrd() => Registers.AX = 1;

        /// <summary>
        ///     sprintf-like string formatter utility
        ///
        ///     Main differentiation is that spr() supports long integer and floating point conversions
        /// </summary>
        /// <returns></returns>
        public ushort spr()
        {
            var sourceOffset = GetParameter(0);
            var sourceSegment = GetParameter(1);

            var output = Module.Memory.GetString(sourceSegment, sourceOffset);


            //If the supplied string has any control characters for formatting, process them
            var formattedMessage = FormatPrintf(output, 2);

            if (formattedMessage.Length > 0x400)
                throw new OutOfMemoryException($"SPR write is > 1k ({formattedMessage.Length}) and would overflow pre-allocated buffer");

            if (!HostMemoryVariables.TryGetValue("SPR", out var variablePointer))
            {
                //allocate 1k for the SPR buffer
                var offset = Module.Memory.AllocateHostMemory(0x400);

                variablePointer = new IntPtr16((ushort)EnumHostSegments.HostMemorySegment, offset);
                HostMemoryVariables["SPR"] = variablePointer;
            }

            Module.Memory.SetArray(variablePointer.Segment, variablePointer.Offset, formattedMessage);

#if DEBUG
            _logger.Info($"Added {output.Length} bytes to the buffer");
#endif

            Registers.AX = variablePointer.Offset;
            Registers.DX = variablePointer.Segment;

            return 0;
        }

        /// <summary>
        ///     Long Multiplication (Borland C++ Implicit Function)
        /// 
        /// </summary>
        /// <returns></returns>
        public ushort f_lxmul()
        {

            var value1 = (Registers.DX << 16) | Registers.AX;
            var value2 = (Registers.CX << 16) | Registers.BX;

            var result = value1 * value2;

#if DEBUG
            _logger.Info($"Performed Long Multiplication {value1}*{value2}={result}");
#endif

            Registers.DX = (ushort)(result >> 16);
            Registers.AX = (ushort)(result & 0xFFFF);

            return 0;
        }

        /// <summary>
        ///     Long Division (Borland C++ Implicit Function)
        ///
        ///     Input: Two long values on stack (arg1/arg2)
        ///     Output: DX:AX = quotient
        ///             DI:SI = remainder
        /// </summary>
        /// <returns></returns>
        public ushort f_ldiv()
        {

            var arg1 = (GetParameter(1) << 16) | GetParameter(0);
            var arg2 = (GetParameter(3) << 16) | GetParameter(2);

            var quotient = Math.DivRem(arg1, arg2, out var remainder);

#if DEBUG
            _logger.Info($"Performed Long Division {arg1}/{arg2}={quotient} (Remainder: {remainder})");
#endif

            Registers.DX = (ushort)(quotient >> 16);
            Registers.AX = (ushort)(quotient & 0xFFFF);

            Registers.DI = (ushort)(remainder >> 16);
            Registers.SI = (ushort)(remainder & 0xFFFF);

            var previousBP = Module.Memory.GetWord(Registers.SS, (ushort)(Registers.BP + 1));
            var previousIP = Module.Memory.GetWord(Registers.SS, (ushort)(Registers.BP + 3));
            var previousCS = Module.Memory.GetWord(Registers.SS, (ushort)(Registers.BP + 5));
            //Set stack back to entry state, minus parameters
            Registers.SP += 14;
            Module.Memory.SetWord(Registers.SS, (ushort)(Registers.SP - 1), previousCS);
            Registers.SP -= 2;
            Module.Memory.SetWord(Registers.SS, (ushort)(Registers.SP - 1), previousIP);
            Registers.SP -= 2;
            Module.Memory.SetWord(Registers.SS, (ushort)(Registers.SP - 1), previousBP);
            Registers.SP -= 2;
            Registers.BP = Registers.SP;
            return 0;
        }

        /// <summary>
        ///     Writes formatted data from variable argument list to string
        ///
        ///     similar to prf, but the destination is a char*, but the output buffer
        /// </summary>
        /// <returns></returns>
        public ushort vsprintf()
        {
            var targetOffset = GetParameter(0);
            var targetSegment = GetParameter(1);
            var formatOffset = GetParameter(2);
            var formatSegment = GetParameter(3);

            var formatString = Module.Memory.GetString(formatSegment, formatOffset);
            
            //If the supplied string has any control characters for formatting, process them
            var formattedMessage = FormatPrintf(formatString, 4, true);

            
            Module.Memory.SetArray(targetSegment, targetOffset, formattedMessage);

            return 0;
        }

        /// <summary>
        ///     Scans the specified MDF file for a line prefix that matches the specified string
        /// 
        ///     Signature: char *scnmdf(char *mdfnam,char *linpfx);
        ///     Returns: AX = Offset of String
        ///              DX = Segment of String
        /// </summary>
        /// <returns></returns>
        public ushort scnmdf()
        {
            var mdfnameOffset = GetParameter(0);
            var mdfnameSegment = GetParameter(1);
            var lineprefixOffset = GetParameter(2);
            var lineprefixSegment = GetParameter(3);

            var mdfNameBytes = Module.Memory.GetString(mdfnameSegment, mdfnameOffset);
            var mdfName = Encoding.ASCII.GetString(mdfNameBytes);

            var lineprefixBytes = Module.Memory.GetString(lineprefixSegment, lineprefixOffset);
            var lineprefix = Encoding.ASCII.GetString(lineprefixBytes);

            //Setup Host Memory Variables Pointer
            if (!HostMemoryVariables.TryGetValue($"SCNMDF", out var variablePointer))
            {
                var offset = Module.Memory.AllocateHostMemory(256);
                variablePointer = new IntPtr16((ushort)EnumHostSegments.HostMemorySegment, offset);
            }

            var recordFound = false;
            foreach (var line in File.ReadAllLines($"{Module.ModulePath}{mdfName}"))
            {
                if (line.StartsWith(lineprefix))
                {
                    var result = Encoding.ASCII.GetBytes(line.Split(':')[1] + "\0");

                    if (result.Length > 256)
                        throw new OverflowException("SCNMDF result is > 256 bytes");

                    Module.Memory.SetArray(variablePointer.Segment, variablePointer.Offset, result);
                    recordFound = true;
                    break;
                }
            }

            //Write Null String to address if nothing was found
            if(!recordFound)
                Module.Memory.SetByte(variablePointer.Segment, variablePointer.Offset, 0x0);

            Registers.AX = variablePointer.Segment;
            Registers.DX = variablePointer.Offset;

            return 0;
        }

        /// <summary>
        ///     Returns the time of day it is bitwise HHHHHMMMMMMSSSSS coding
        ///
        ///     Signature: int time=now()
        /// </summary>
        /// <returns></returns>
        public ushort now()
        {
            //From DOSFACE.H:
            //#define dttime(hour,min,sec) (((hour)<<11)+((min)<<5)+((sec)>>1))
            var packedTime = (DateTime.Now.Hour << 11) + (DateTime.Now.Minute << 5) + (DateTime.Now.Second >> 1);

#if DEBUG
            _logger.Info($"Returned packed time: {packedTime}");
#endif

            Registers.AX = (ushort)packedTime;

            return 0;
        }

        /// <summary>
        ///     Modulo, non-significant (Borland C++ Implicit Function)
        ///
        ///     Signature: DX:AX = arg1 % arg2
        /// </summary>
        /// <returns></returns>
        public ushort f_lumod()
        {
            var arg1 = (GetParameter(1) << 16) | GetParameter(0);
            var arg2 = (GetParameter(3) << 16) | GetParameter(2);

            var result = arg1 % arg2;

            Registers.DX = (ushort) (result >> 16);
            Registers.AX = (ushort) (result & 0xFFFF);


            var previousBP = Module.Memory.GetWord(Registers.SS, (ushort) (Registers.BP + 1));
            var previousIP = Module.Memory.GetWord(Registers.SS, (ushort) (Registers.BP + 3));
            var previousCS = Module.Memory.GetWord(Registers.SS, (ushort) (Registers.BP + 5));
            //Set stack back to entry state, minus parameters
            Registers.SP += 14;
            Module.Memory.SetWord(Registers.SS, (ushort) (Registers.SP - 1), previousCS);
            Registers.SP -= 2;
            Module.Memory.SetWord(Registers.SS, (ushort) (Registers.SP - 1), previousIP);
            Registers.SP -= 2;
            Module.Memory.SetWord(Registers.SS, (ushort) (Registers.SP - 1), previousBP);
            Registers.SP -= 2;
            Registers.BP = Registers.SP;
            return 0;
        }

        /// <summary>
        ///     Set a block of memory to a value
        ///
        ///     Signature: void setmem(char *destination, unsigned nbytes, char value)
        /// </summary>
        /// <returns></returns>
        private ushort setmem()
        {
            var destinationOffset = GetParameter(0);
            var destinationSegment = GetParameter(1);
            var numberOfBytesToWrite = GetParameter(2);
            var byteToWrite = GetParameter(3);

            for (var i = 0; i < numberOfBytesToWrite; i++)
            {
                Module.Memory.SetByte(destinationSegment, (ushort) (destinationOffset +i), (byte)byteToWrite);
            }

#if DEBUG
            _logger.Info($"Set {numberOfBytesToWrite} bytes to {byteToWrite:X2} startint at {destinationSegment:X4}:{destinationOffset:X4}");
#endif
            return 0;
        }

        /// <summary>
        ///     Output buffer of prf() and prfmsg()
        ///
        ///     Because it's a pointer, the segment only contains Int16:Int16 pointer to the actual prfbuf segment
        ///
        ///     Signature: char *prfbuf
        /// </summary>
        private ushort prfbuf()
        {
            Module.Memory.SetArray((ushort)EnumHostSegments.Prfbuf, 0, _outputBuffer.ToArray());
            return (ushort) EnumHostSegments.PrfbufPointer;
        }

        /// <summary>
        ///     Copies characters from a string
        ///
        ///     Signature: char *strncpy(char *destination, const char *source, size_t num)
        /// </summary>
        /// <returns></returns>
        private ushort strncpy()
        {
            var destinationOffset = GetParameter(0);
            var destinationSegment = GetParameter(1);
            var sourceOffset = GetParameter(2);
            var sourceSegment = GetParameter(3);
            var numberOfBytesToCopy = GetParameter(4);

            var sourceString = Module.Memory.GetString(sourceSegment, sourceOffset);

            for (var i = 0; i < numberOfBytesToCopy; i++)
            {
                if (sourceString[i] == 0x0)
                {
                    //Write remaining nulls
                    for (var j = i; j < numberOfBytesToCopy; j++)
                        Module.Memory.SetByte(destinationSegment, (ushort) (destinationOffset + j), 0x0);

                    break;
                }
                Module.Memory.SetByte(destinationSegment, (ushort) (destinationOffset + i), sourceString[i]);
            }

#if DEBUG
            _logger.Info($"Copied {numberOfBytesToCopy} from {sourceSegment:X4}:{sourceOffset:X4} to {destinationSegment:X4}:{destinationOffset:X4}");
#endif
            return 0;
        }

        private ushort register_textvar()
        {
            var textOffset = GetParameter(0);
            var textSegment = GetParameter(1);
            var destinationOffset = GetParameter(2);
            var destinationSegment = GetParameter(3);

            var functionPointer = new IntPtr16(destinationSegment, destinationOffset);
            var textBytes = Module.Memory.GetString(textSegment, textOffset);

            _textVariables.Add(textBytes.ToArray(), functionPointer);

#if DEBUG
            _logger.Info($"Registered Textvar \"{Encoding.ASCII.GetString(textBytes)}\" to {destinationSegment:X4}:{destinationOffset:X4}");
#endif
            return 0;
        }

        /// <summary>
        ///     As best I can tell, this routine performs a GetEqual based on the key specified
        ///
        ///     Signature: int obtbtvl (void *recptr, void *key, int keynum, int obtopt, int loktyp)
        ///     Returns: AX == 0 record not found, 1 record found
        /// </summary>
        /// <returns></returns>
        private ushort obtbtvl()
        {
            var recordPointerOffset = GetParameter(0);
            var recordPointerSegment = GetParameter(1);
            var keyOffset = GetParameter(2);
            var keySegment = GetParameter(3);
            var keyNum = GetParameter(4);
            var obtopt = GetParameter(5);
            var lockType = GetParameter(6);

            ushort result = 0;
            switch (obtopt)
            {
                //GetEqual
                case 5:
                    result = (ushort) (_currentBtrieveFile.GetRecordByKey(Module.Memory.GetString(keySegment, keyOffset)) ? 1 : 0);
                    break;
            }

            Registers.AX = result;

            return 0;
        }

        /// <summary>
        ///     Declare size of the Volatile Data Area (Maximum size the module will require)
        ///     Because this is just another memory block, we use the host memory
        /// 
        ///     Signature: char *alczer(unsigned nbytes);
        ///     Return: AX = Offset in Segment (host)
        ///             DX = Data Segment
        /// </summary>
        public ushort dclvda()
        {
            var size = GetParameter(0);

            //Get the current pointer
            var pointer = Module.Memory.AllocateHostMemory(size);

            Registers.AX = pointer;
            Registers.DX = (ushort)EnumHostSegments.HostMemorySegment;

#if DEBUG
            _logger.Info($"Allocated {size} bytes starting at {pointer:X4}");
#endif

            return 0;
        }

        public ushort nterms() => (ushort) EnumHostSegments.Nterms;
    }
}