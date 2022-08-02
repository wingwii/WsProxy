using System;
using System.Collections.Generic;
using System.Text;

namespace client
{
    class BufferReader
    {
        private Queue<byte> _data = new Queue<byte>();

        public BufferReader()
        {

        }

        public int Available
        {
            get
            {
                return this._data.Count;
            }
        }

        public void Clear()
        {
            this._data.Clear();
        }

        public void Feed(byte[] buf, int offset, int size)
        {
            for (int i = 0; i < size; ++i)
            {
                this._data.Enqueue(buf[offset + i]);
            }
        }

        public byte ReadByte()
        {
            return this._data.Dequeue();
        }

        public byte[] PickRemainData()
        {
            var n = this._data.Count;
            if (n <= 0)
            {
                return null;
            }

            var result = new byte[n];
            for (int i = 0; i < n; ++i)
            {
                var b = this._data.Dequeue();
                result[i] = b;
            }
            return result;
        }


    }
}
