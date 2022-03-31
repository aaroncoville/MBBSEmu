﻿using MBBSEmu.IO;
using MBBSEmu.Logging;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MBBSEmu.Module
{
    /// <summary>
    ///     Parses a given Worldgroup/MajorBBS MSG file and outputs a compiled MCV file
    ///
    ///     Only one language is supported, so anything other than English/ANSI will be
    ///     ignored.
    /// </summary>
    public class MsgFile
    {
        protected static readonly Logger _logger = LogManager.GetCurrentClassLogger(typeof(CustomLogger));
        private readonly string _modulePath;
        private readonly string _moduleName;
        private readonly IFileUtility _fileUtility;

        public readonly string FileName;
        public readonly string FileNameAtRuntime;

        public readonly Dictionary<string, byte[]> MsgValues;

        public MsgFile(IFileUtility fileUtility, string modulePath, string msgName)
        {
            MsgValues = new Dictionary<string, byte[]>();
            _modulePath = modulePath;
            _moduleName = msgName;
            _fileUtility = fileUtility;

            FileName = $"{msgName.ToUpper()}.MSG";
            FileNameAtRuntime = $"{msgName.ToUpper()}.MCV";

            _logger.Debug($"({_moduleName}) Compiling MCV from {FileName}");
            BuildMCV();
        }

        public enum MsgParseState
        {
            /// <summary>
            ///     Data Before Identifier Characters
            /// </summary>
            PREKEY,

            /// <summary>
            ///     Data for Identifier
            /// </summary>
            KEY,

            /// <summary>
            ///     Data Post Identifier
            /// </summary>
            POSTKEY,

            /// <summary>
            ///     Data In Value
            /// </summary>
            VALUE,

            /// <summary>
            ///     Post Value (Type, Length, etc.)
            /// </summary>
            POSTVALUE,

            /// <summary>
            ///     If an escaped bracket is encountered as part of a message
            /// </summary>
            ESCAPEBRACKET,
        };

        private static bool IsIdentifier(char c) =>
            char.IsDigit(c) || (c is >= 'A' and <= 'Z');

        private static bool IsAlnum(char c) =>
            char.IsLetterOrDigit(c);

        private static MemoryStream FixLineEndings(MemoryStream input)
        {
            var rawMessage = input.ToArray();
            input.SetLength(0);

            for (var i = 0; i < rawMessage.Length; ++i)
            {
                var c = rawMessage[i];
                if (c == '\n')
                {
                    var next = i < (rawMessage.Length - 1) ? (char)rawMessage[i + 1] : (char)0;
                    if (!IsAlnum(next) && next != '%' && next != '(' && next != '"')
                    {
                        input.WriteByte((byte)'\r');
                        continue;
                    }
                }
                input.WriteByte(c);
            }
            input.WriteByte(0); //Null Terminate
            return input;
        }

        /// <summary>
        ///     Takes the Specified MSG File and compiles a MCV file to be used at runtime
        /// </summary>
        private void BuildMCV()
        {
            var path = _fileUtility.FindFile(_modulePath, Path.ChangeExtension(_moduleName, ".MSG"));
            var msgFileData = File.ReadAllBytes(Path.Combine(_modulePath, path));
            var language = Encoding.ASCII.GetBytes("English/ANSI\0");

            var messages = ExtractMsgValues(msgFileData);

            WriteMCV(language, messages);
        }

        private static IList<byte[]> ExtractMsgValues(ReadOnlySpan<byte> msgData)
        {
            var result = new List<byte[]>();

            var state = MsgParseState.PREKEY;
            var msgKey = new StringBuilder();
            using var msgValue = new MemoryStream();
            var previousCharacter = (char)0;

            foreach (var b in msgData)
            {
                var c = (char)b;
                switch (state)
                {
                    case MsgParseState.PREKEY:
                        {
                            c = ProcessPreKey(c, out state);

                            if (c > 0)
                                msgKey.Append(c);

                            break;
                        }
                    case MsgParseState.KEY:
                        {
                            c = ProcessKey(c, out state);

                            if (c > 0)
                                msgKey.Append(c);

                            break;
                        }
                    case MsgParseState.POSTKEY:
                        {
                            ProcessPostKey(c, out state);

                            //Reset Key
                            if (state == MsgParseState.KEY)
                            {
                                msgKey.Clear();
                                msgKey.Append(c);
                            }

                            break;
                        }
                    case MsgParseState.VALUE:
                        {
                            c = ProcessValue(c, previousCharacter, out state);

                            if (state == MsgParseState.ESCAPEBRACKET)
                            {
                                EscapeBracket(msgValue);
                                state = MsgParseState.VALUE;
                                break;
                            }

                            //End of Value, Write to Output
                            if (c == 0 && state == MsgParseState.POSTVALUE)
                            {
                                if (msgKey.ToString().ToUpper() == "LANGUAGE")
                                {
                                    //Ignore for now, it's always "English/ANSI"
                                }
                                else
                                {
                                    result.Add(FixLineEndings(msgValue).ToArray());
                                }

                                //Reset Buffers
                                msgValue.SetLength(0);
                                msgKey.Clear();
                                break;
                            }

                            if (c > 0)
                                msgValue.WriteByte((byte)c);
                            break;
                        }
                    case MsgParseState.POSTVALUE:
                        {
                            ProcessPostValue(c, out state);
                            break;
                        }
                    default:
                        break;
                }

                previousCharacter = c;
            }

            return result;
        }

        /// <summary>
        ///     Handles processing of the characters prior to a Key in an entry in a MSG file
        /// </summary>
        /// <param name="inputCharacter"></param>
        /// <param name="resultState"></param>
        /// <returns></returns>
        public static char ProcessPreKey(char inputCharacter, out MsgParseState resultState)
        {
            if (IsIdentifier(inputCharacter))
            {
                resultState = MsgParseState.KEY;
                return inputCharacter;
            }

            resultState = MsgParseState.PREKEY;
            return (char)0;
        }

        /// <summary>
        ///     Handles processing of the characters in a Key of an entry in a MSG File
        /// </summary>
        /// <param name="inputCharacter"></param>
        /// <param name="resultState"></param>
        /// <returns></returns>
        public static char ProcessKey(char inputCharacter, out MsgParseState resultState)
        {
            if (IsIdentifier(inputCharacter))
            {
                resultState = MsgParseState.KEY;
                return inputCharacter;
            }

            resultState = MsgParseState.POSTKEY;
            return (char)0;
        }

        /// <summary>
        ///     Handles processing the characters after the Key of an entry in a MSG File
        /// </summary>
        /// <param name="inputCharacter"></param>
        /// <param name="resultState"></param>
        public static void ProcessPostKey(char inputCharacter, out MsgParseState resultState)
        {
            if (inputCharacter == '{')
            {
                resultState = MsgParseState.VALUE;
                return;
            }

            //If we find a character that's an key value in Post Key, we're probably processing a text block so reset
            if (IsIdentifier(inputCharacter))
            {
                resultState = MsgParseState.KEY;
                return;
            }

            resultState = MsgParseState.POSTKEY;
        }

        /// <summary>
        ///     Handles processing the values between the curly brackets in an MSG File
        /// </summary>
        /// <param name="inputCharacter"></param>
        /// <param name="previousCharacter"></param>
        /// <param name="resultState"></param>
        /// <returns></returns>
        public static char ProcessValue(char inputCharacter, char previousCharacter, out MsgParseState resultState)
        {
            if (inputCharacter == '}')
            {
                //Escaped Bracket
                if (previousCharacter == '~')
                {
                    resultState = MsgParseState.ESCAPEBRACKET;
                    return (char)0;
                }

                //Valid Ending Bracket
                resultState = MsgParseState.POSTVALUE;
                return (char)0;
            }

            resultState = MsgParseState.VALUE;

            //Escaped Tilde
            if (inputCharacter == '~' && previousCharacter == '~')
                return (char)0;

            return inputCharacter;
        }

        /// <summary>
        ///     Handles processing the characters after the final value curly bracket in an MSG file
        /// </summary>
        /// <param name="inputCharacter"></param>
        /// <param name="resultState"></param>
        public static void ProcessPostValue(char inputCharacter, out MsgParseState resultState)
        {
            if (inputCharacter == '\n')
            {
                resultState = MsgParseState.PREKEY;
                return;
            }

            resultState = MsgParseState.POSTVALUE;
        }

        private static void EscapeBracket(MemoryStream input)
        {
            var arrayToParse = input.ToArray();
            input.SetLength(0);
            input.Write(arrayToParse[..^2]);
            input.WriteByte((byte)'}');
        }

        private void WriteUInt32(Stream stream, int value) => stream.Write(BitConverter.GetBytes(value));
        private void WriteUInt16(Stream stream, short value) => stream.Write(BitConverter.GetBytes(value));

        private void WriteMCV(byte[] language, IList<byte[]> messages, bool writeStringLengthTable = true)
        {
            if (language.Length == 0)
                throw new ArgumentException("Empty language, bad MSG parse");

            /*
            * 16-byte final header is
            *
            * uint32_t ;   // ptr to languages
            * uint32_t ;   // ptr to length array, always last
            * uint32_t ;   // ptr to location list
            * uint16_t ;   // count of languages
            * uint16_t ;   // count of messages
            */
            using var writer = File.Open(Path.Combine(_modulePath, FileNameAtRuntime), FileMode.Create);
            var offsets = new int[messages.Count];

            // write out the languages (just messages[0])
            writer.Write(language);

            var offset = language.Length;

            // write out each string with a null-terminator
            var i = 0;
            foreach (var message in messages)
            {
                offsets[i++] = offset;

                writer.Write(message);
                offset += message.Length;
            }

            var numberOfLanguages = 1;
            var stringLengthArrayPointer = 0;

            // write out the offset table first
            var stringLocationsPointer = offset;
            foreach (var msgOffset in offsets)
                WriteUInt32(writer, msgOffset);

            offset += 4 * offsets.Length;

            // write out the file lengths if requested
            if (writeStringLengthTable)
            {
                stringLengthArrayPointer = offset;

                foreach (var message in messages)
                    WriteUInt32(writer, message.Length + 1);

                // don't need to update offset since it's no longer referenced past this point
            }
            else
            {
                // ptr to location list
                WriteUInt32(writer, stringLocationsPointer);
            }

            // language pointer, always 0
            WriteUInt32(writer, 0);
            // length array
            WriteUInt32(writer, stringLengthArrayPointer);
            // ptr to location list
            WriteUInt32(writer, stringLocationsPointer);
            // count of languages
            WriteUInt16(writer, (short)numberOfLanguages);
            // count of messages
            WriteUInt16(writer, (short)messages.Count);
        }

        public static void UpdateValues(string filename, Dictionary<string, string> values)
        {
            var tmpPath = Path.GetTempFileName();
            var input = new StreamStream(new FileStream(filename, FileMode.Open));
            var output = new StreamStream(new FileStream(tmpPath, FileMode.OpenOrCreate));

            UpdateValues(input, output, values);

            input.Dispose();
            output.Dispose();

            File.Move(tmpPath, filename, overwrite: true);
        }

        public static void UpdateValues(IStream input, IStream output, Dictionary<string, string> values)
        {
            //Read The Full Input
            var inputData = new MemoryStream();
            var inputByte = input.ReadByte();
            while (inputByte != -1)
            {
                inputData.WriteByte((byte)inputByte);
                inputByte = input.ReadByte();
            }

            //foreach(var e in ExtractMsgValues(inputData.ToArray(), values))
                //output.Write(e);
        }
    }
}
