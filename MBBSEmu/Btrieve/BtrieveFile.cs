﻿using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace MBBSEmu.Btrieve
{
    /// <summary>
    ///     Represents an instance of a Btrieve File (both Legacy .DAT and new .EMU)
    /// </summary>
    public class BtrieveFile
    {
        /// <summary>
        ///     Filename of Btrieve File
        /// </summary>
        public string FileName { get; set; }

        /// <summary>
        ///     Number of Pages within the Btrieve File
        /// </summary>
        [JsonIgnore]
        public ushort PageCount => (ushort) (Data.Length / PageLength - 1);

        private ushort _recordCount;
        /// <summary>
        ///     Total Number of Records in the specified Btrieve File
        /// </summary>
        public ushort RecordCount
        {
            get
            {
                if (Data?.Length > 0)
                    return BitConverter.ToUInt16(Data, 0x1C);

                return _recordCount;
            }
            set
            {
                if (Data?.Length > 0)
                    Array.Copy(BitConverter.GetBytes(value), 0, Data, 0x1C, sizeof(ushort));

                _recordCount = value;
            }
        }

        private ushort _recordLength;
        /// <summary>
        ///     Defined Length of the records within the Btrieve File
        /// </summary>
        public ushort RecordLength
        {
            get
            {
                if (Data?.Length > 0)
                    return BitConverter.ToUInt16(Data, 0x16);

                return _recordLength;
            }
            set
            {
                if (Data?.Length > 0)
                    Array.Copy(BitConverter.GetBytes(value), 0, Data, 0x16, sizeof(ushort));

                _recordLength = value;
            }
        }

        private ushort _physicalRecordLength;
        /// <summary>
        ///     Actual Length of the records within the Btrieve File, including additional padding.
        /// </summary>
        public ushort PhysicalRecordLength
        {
            get
            {
                if (Data?.Length > 0)
                    return BitConverter.ToUInt16(Data, 0x18);

                return _physicalRecordLength;
            }
            set
            {
                if (Data?.Length > 0)
                    Array.Copy(BitConverter.GetBytes(value), 0, Data, 0x18, sizeof(ushort));

                _physicalRecordLength = value;
            }
        }

        private ushort _pageLength;
        /// <summary>
        ///     Defined length of each Page within the Btrieve File
        /// </summary>
        public ushort PageLength
        {
            get
            {
                if (Data?.Length > 0)
                    return BitConverter.ToUInt16(Data, 0x08);

                return _pageLength;
            }
            set
            {
                if (Data?.Length > 0)
                    Array.Copy(BitConverter.GetBytes(value), 0, Data, 0x08, sizeof(ushort));

                _pageLength = value;
            }
        }


        private ushort _keyCount;
        /// <summary>
        ///     Number of Keys defined within the Btrieve File
        /// </summary>
        public ushort KeyCount
        {
            get
            {
                if (Data?.Length > 0)
                    return BitConverter.ToUInt16(Data, 0x14);

                return _keyCount;
            }
            set
            {
                if (Data?.Length > 0)
                    Array.Copy(BitConverter.GetBytes(value), 0, Data, 0x14, sizeof(ushort));

                _keyCount = value;
            }
        }

        /// <summary>
        ///     Records appear to have a potential padding at the end of the records.
        ///
        ///     If we detect it, we can modify this value
        /// </summary>
        public int RecordPadding { get; set; }

        /// <summary>
        ///     Raw contents of Btrieve File
        /// </summary>
        [JsonIgnore]
        public byte[] Data { get; }

        /// <summary>
        ///     Btrieve Records
        /// </summary>
        public List<BtrieveRecord> Records { get; set; }

        /// <summary>
        ///     Btrieve Keys
        /// </summary>
        public Dictionary<ushort, BtrieveKey> Keys { get; set; }

        /// <summary>
        ///     Log Key is an internal value used by the Btrieve engine to track unique
        ///     records -- it adds 8 bytes to the end of the record that's not accounted for
        ///     in the RecordLength definition. We need to know if it's present to properly
        ///     offset records when loading
        /// </summary>
        public bool LogKeyPresent { get; set; }

        public BtrieveFile()
        {
            Records = new List<BtrieveRecord>();
            Keys = new Dictionary<ushort, BtrieveKey>();
        }

        public BtrieveFile(ReadOnlySpan<byte> fileContents) : this()
        {
            Data = fileContents.ToArray();
        }
    }
}
