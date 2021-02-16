﻿using Dalamud.Data;
using Lumina.Excel.GeneratedSheets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NoSoliciting {
    public static class FilterUtil {
        private static readonly Dictionary<char, string> Replacements = new() {
            // numerals
            ['\ue055'] = "1",
            ['\ue056'] = "2",
            ['\ue057'] = "3",
            ['\ue058'] = "4",
            ['\ue059'] = "5",
            ['\ue099'] = "10",
            ['\ue09a'] = "11",
            ['\ue09b'] = "12",
            ['\ue09c'] = "13",
            ['\ue09d'] = "14",
            ['\ue09e'] = "15",
            ['\ue09f'] = "16",
            ['\ue0a0'] = "17",
            ['\ue0a1'] = "18",
            ['\ue0a2'] = "19",
            ['\ue0a3'] = "20",
            ['\ue0a4'] = "21",
            ['\ue0a5'] = "22",
            ['\ue0a6'] = "23",
            ['\ue0a7'] = "24",
            ['\ue0a8'] = "25",
            ['\ue0a9'] = "26",
            ['\ue0aa'] = "27",
            ['\ue0ab'] = "28",
            ['\ue0ac'] = "29",
            ['\ue0ad'] = "30",
            ['\ue0ae'] = "31",

            // symbols
            ['\ue0af'] = "+",
            ['\ue070'] = "?",

            // letters in other sets
            ['\ue022'] = "A",
            ['\ue024'] = "_A",
            ['\ue0b0'] = "E",
        };

        private const char LowestReplacement = '\ue022';

        public static string Normalise(string input) {
            if (input == null) {
                throw new ArgumentNullException(nameof(input), "input cannot be null");
            }

            // replace ffxiv private use chars
            var builder = new StringBuilder(input.Length);
            foreach (var c in input) {
                if (c < LowestReplacement) {
                    goto AppendNormal;
                }

                // alphabet
                if (c >= 0xe071 && c <= 0xe08a) {
                    builder.Append((char) (c - 0xe030));
                    continue;
                }

                // 0 to 9
                if (c >= 0xe060 && c <= 0xe069) {
                    builder.Append((char) (c - 0xe030));
                    continue;
                }

                // 1 to 9
                if (c >= 0xe0b1 && c <= 0xe0b9) {
                    builder.Append((char) (c - 0xe080));
                    continue;
                }

                // 1 to 9 again
                if (c >= 0xe090 && c <= 0xe098) {
                    builder.Append((char) (c - 0xe05f));
                    continue;
                }

                // replacements in map
                if (Replacements.TryGetValue(c, out var rep)) {
                    builder.Append(rep);
                    continue;
                }

                AppendNormal:
                builder.Append(c);
            }

            input = builder.ToString();

            // NFKD unicode normalisation
            return input.Normalize(NormalizationForm.FormKD);
        }

        private static int MaxItemLevel { get; set; }

        private enum Slot {
            MainHand,
            OffHand,
            Head,
            Chest,
            Hands,
            Waist,
            Legs,
            Feet,
            Earrings,
            Neck,
            Wrist,
            RingL,
            RingR,
        }

        private static Slot? SlotFromItem(Item item) {
            var cat = item.EquipSlotCategory.Value;
            if (cat == null) {
                return null;
            }

            if (cat.MainHand != 0) {
                return Slot.MainHand;
            }

            if (cat.Head != 0) {
                return Slot.Head;
            }

            if (cat.Body != 0) {
                return Slot.Chest;
            }

            if (cat.Gloves != 0) {
                return Slot.Hands;
            }

            if (cat.Waist != 0) {
                return Slot.Waist;
            }

            if (cat.Legs != 0) {
                return Slot.Legs;
            }

            if (cat.Feet != 0) {
                return Slot.Feet;
            }

            if (cat.OffHand != 0) {
                return Slot.OffHand;
            }

            if (cat.Ears != 0) {
                return Slot.Earrings;
            }

            if (cat.Neck != 0) {
                return Slot.Neck;
            }

            if (cat.Wrists != 0) {
                return Slot.Wrist;
            }

            if (cat.FingerL != 0) {
                return Slot.RingL;
            }

            if (cat.FingerR != 0) {
                return Slot.RingR;
            }

            return null;
        }

        public static int MaxItemLevelAttainable(DataManager data) {
            if (MaxItemLevel > 0) {
                return MaxItemLevel;
            }

            if (data == null) {
                throw new ArgumentNullException(nameof(data), "DataManager cannot be null");
            }

            var ilvls = new Dictionary<Slot, int>();

            foreach (var item in data.GetExcelSheet<Item>()) {
                var slot = SlotFromItem(item);
                if (slot == null) {
                    continue;
                }

                var itemLevel = 0;
                var ilvl = item.LevelItem.Value;
                if (ilvl != null) {
                    itemLevel = (int) ilvl.RowId;
                }

                if (ilvls.TryGetValue((Slot) slot, out var currentMax) && currentMax > itemLevel) {
                    continue;
                }

                ilvls[(Slot) slot] = itemLevel;
            }

            MaxItemLevel = (int) ilvls.Values.Average();

            return MaxItemLevel;
        }
    }

    public static class RmtExtensions {
        public static bool ContainsIgnoreCase(this string haystack, string needle) {
            return CultureInfo.InvariantCulture.CompareInfo.IndexOf(haystack, needle, CompareOptions.IgnoreCase) >= 0;
        }
    }
}
