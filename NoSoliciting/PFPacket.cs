﻿using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace NoSoliciting {
    public static class PacketInfo {
        public static readonly int packetSize = Marshal.SizeOf<PFPacket>();
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PFPacket {
        private readonly int unk0;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        private readonly byte[] padding1;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public PFListing[] listings;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PFListing {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        private readonly byte[] header1;

        internal readonly uint id;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        private readonly byte[] header2;

        private readonly uint unknownInt1;
        private readonly ushort unknownShort1;
        private readonly ushort unknownShort2;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        private readonly byte[] header3;

        internal readonly byte category;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        private readonly byte[] header4;

        internal readonly ushort duty;
        internal readonly byte dutyType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 11)]
        private readonly byte[] header5;

        internal readonly ushort world;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        private readonly byte[] header6;

        internal readonly byte objective;
        internal readonly byte beginnersWelcome;
        internal readonly byte conditions;
        internal readonly byte dutyFinderSettings;
        internal readonly byte lootRules;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
        internal readonly byte[] header7; // all zero in every pf I've examined

        internal readonly uint lastPatchHotfixTimestamp; // last time the servers were restarted?
        internal readonly ushort secondsRemaining;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
        private readonly byte[] header8; // 00 00 01 00 00 00 in every pf I've examined

        internal readonly ushort minimumItemLevel;
        internal readonly ushort homeWorld;
        internal readonly ushort currentWorld;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        private readonly byte[] header9; // 02 XX 01 00 in every pf I've examined

        internal readonly byte searchArea;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        private readonly byte[] header10; // 00 01 00 00 00 for every pf except alliance raids where it's 01 03 00 00 00 (second byte # parties?)

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        internal readonly uint[] slots;
        private readonly uint job; // job started as?

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        private readonly byte[] header11; // all zero in every pf I've examined

        // Note that ByValTStr will not work here because the strings are UTF-8 and there's only a CharSet for UTF-16 in C#.
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        private readonly byte[] name;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 192)]
        private readonly byte[] description;

        // 128 (0x80) before name and desc
        // 160 (0xA0) with name (32 bytes/0x20)
        // 352 (0x160) with both (192 bytes/0xC0)

        private static string HandleString(byte[] bytes) {
            byte[] nonNull = bytes.TakeWhile(b => b != 0).ToArray();
            return Encoding.UTF8.GetString(nonNull);
        }

        internal string Name() {
            return HandleString(this.name);
        }

        internal string Description() {
            return HandleString(this.description);
        }

        internal bool IsNull() {
            // a valid party finder must have at least one slot set
            return this.slots.All(slot => slot == 0);
        }
    }
}
