/**
 *  Monk
 *  Copyright (C) Timothy Baxendale
 *
 *  This program is free software; you can redistribute it and/or modify
 *  it under the terms of the GNU General Public License as published by
 *  the Free Software Foundation; either version 2 of the License, or
 *  (at your option) any later version.
 *
 *  This program is distributed in the hope that it will be useful,
 *  but WITHOUT ANY WARRANTY; without even the implied warranty of
 *  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 *  GNU General Public License for more details.
 *
 *  You should have received a copy of the GNU General Public License along
 *  with this program; if not, write to the Free Software Foundation, Inc.,
 *  51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
**/
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Linq;
using Monk.Bittwiddling;

namespace Monk.Imaging
{
    public class Steganographer : IDisposable
    {
        public Seed Seed { get; set; }

        private int lsb = 1;
        public int LeastSignificantBits
        {
            get {
                return lsb;
            }
            set {
                if (lsb < 1 || lsb > 4) {
                    throw new ArgumentException("Least significant bits must be between 1 and 4");
                }
                lsb = value;
            }
        }

        public Bitmap BitmapImage { get; set; }
        public PixelColor Color { get; set; }

        public bool InvertPrefixBits { get; set; } = false;
        public bool InvertDataBits { get; set; } = false;
        public bool ZeroBasedSize { get; set; } = false;
        public EndianMode Endianness { get; set; } = EndianMode.BigEndian;

        public bool Disposed { get; private set; }

        public Steganographer(string filename)
        {
            BitmapImage = new Bitmap(filename);
        }

        public Steganographer(Bitmap image)
        {
            BitmapImage = image;
        }

        private bool MessageFitsImage(ICollection<byte> message)
        {
            return GetRequiredSize(message) < GetMaximumSize();
        }

        public IEnumerable<ImageChange> Encode(byte[] message, string savePath)
        {
            if (!MessageFitsImage(message)) {
                throw new ArgumentException("Message is too big to encode in the image.");
            }

            BinaryList bits = new BinaryList(BitConverter.GetBytes(message.Length));

            if (InvertDataBits && InvertPrefixBits) {
                bits.AddRange(message);
                bits.Invert();
            }
            else {
                if (InvertPrefixBits) {
                    bits.Invert();
                    bits.AddRange(message);
                }
                else if (InvertDataBits) {
                    BinaryList databin = new BinaryList(message);
                    databin.Invert();
                    bits.AddRange(databin);
                }
            }

            LockedBitmap lockedBmp = LockedBitmap.CreateLockedBitmap(BitmapImage);
            lockedBmp.LockBits();

            IList<ImageChange> changes = new List<ImageChange>();

            int bitIndex = 0;
            for (int pixelIndex = Seed[0]; pixelIndex <= BitmapImage.Height * BitmapImage.Width && bitIndex < bits.Count; pixelIndex += Seed[bitIndex % Seed.Count] + 1) {
                int x = pixelIndex % lockedBmp.Width;
                int y = (pixelIndex - x) / lockedBmp.Width;
                BinaryOctet oldval = lockedBmp.GetPixelColor(x, y, Color);
                BinaryOctet newval = oldval;
                for (int currBit = 0; currBit < lsb; ++currBit)
                    newval = newval.SetBit(currBit, bits[bitIndex++]);

                if (newval != oldval) {
                    changes.Add(new ImageChange(x, y, oldval, newval));
                }

                lockedBmp.SetPixelColor(x, y, newval, Color);
            }

            lockedBmp.UnlockBits();

            BitmapImage.Save(savePath, ImageFormat.Png);

            return changes;
        }

        public int CheckSize()
        {
            int size = BitConverter.ToInt32(ReadBits(sizeof(int), InvertPrefixBits).ToArray(), 0);

            if (size <= 0 || size > GetMaximumSize())
                return -1; // size was invalid, there probably isn't a message

            return size;
        }

        public byte[] Decode()
        {
            int size = CheckSize();

            if (size < 0)
                return null; // no message

            if (ZeroBasedSize) {
                size += 1;
            }

            IEnumerable<byte> data = ReadBits(sizeof(int) + size, InvertDataBits);
            return data.Skip(sizeof(int)).ToArray();
        }

        private IEnumerable<byte> ReadBits(int byteCount, bool invert)
        {
            BinaryList data = new BinaryList();

            LockedBitmap lockedBmp = LockedBitmap.CreateLockedBitmap(BitmapImage);
            lockedBmp.LockBits();

            int bitIndex = 0;
            for (int pixelIndex = Seed[0]; bitIndex < byteCount * 8; pixelIndex += Seed[bitIndex % Seed.Count] + 1) {
                int x = pixelIndex % lockedBmp.Width;
                int y = (pixelIndex - x) / lockedBmp.Width;
                BinaryOctet octet = lockedBmp.GetPixelColor(x, y, Color);

                for (byte currBit = 0; currBit < lsb; ++currBit, ++bitIndex)
                    data.Add(octet[currBit]);
            }

            lockedBmp.UnlockBits();
            if (invert) {
                data.Invert();
            }
            return data.ToBytes(Endianness);
        }

        public int GetRequiredSize(ICollection<byte> message)
        {
            int size = message.Count + sizeof(int);
            int newSize = 0;
            for (int i = 0; i < size; ++i) {
                newSize += Seed[i % Seed.Count] + 1;
            }
            return newSize;
        }

        public int GetMaximumSize()
        {
            return BitmapImage.Width * BitmapImage.Width * LeastSignificantBits;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing) {
                if (BitmapImage != null) {
                    BitmapImage.Dispose();
                    BitmapImage = null;
                }
                Disposed = true;
            }
        }
    }
}

