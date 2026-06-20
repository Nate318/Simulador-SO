using System;
using System.Collections.Generic;
using System.Linq;

namespace SimuladorSO
{
    public class MemoryManager
    {
        public bool[] bitmap;
        public int TotalSize { get; private set; }
        private const int MinBlockSize = 32;

        public MemoryManager(int totalSizeKB)
        {
            TotalSize = totalSizeKB;
            bitmap = new bool[TotalSize / MinBlockSize];
        }

        private int CalculateBlockSize(int required)
        {
            int power = (int)Math.Ceiling(Math.Log(required, 2));
            int block = (int)Math.Pow(2, power);
            return block < MinBlockSize ? MinBlockSize : block;
        }

        public bool AllocateMemory(PCB process, MemoryStrategy strategy)
        {
            int blockSize = CalculateBlockSize(process.RequiredMemory);
            int blocksNeeded = blockSize / MinBlockSize;
            int startIndex = -1;

            var freeHoles = GetFreeHoles(blocksNeeded);
            if (freeHoles.Count == 0) return false;

            switch (strategy)
            {
                case MemoryStrategy.FirstFit:
                    startIndex = freeHoles.First().Key;
                    break;
                case MemoryStrategy.BestFit:
                    startIndex = freeHoles.OrderBy(h => h.Value).First().Key;
                    break;
                case MemoryStrategy.WorstFit:
                    startIndex = freeHoles.OrderByDescending(h => h.Value).First().Key;
                    break;
            }

            for (int i = startIndex; i < startIndex + blocksNeeded; i++)
            {
                bitmap[i] = true;
            }

            process.AssignedMemoryBlockSize = blockSize;
            process.MemoryBaseAddress = startIndex * MinBlockSize;
            return true;
        }

        private Dictionary<int, int> GetFreeHoles(int minBlocks)
        {
            var holes = new Dictionary<int, int>();
            int count = 0, start = -1;
            for (int i = 0; i < bitmap.Length; i++)
            {
                if (!bitmap[i])
                {
                    if (count == 0) start = i;
                    count++;
                }
                else
                {
                    if (count >= minBlocks) holes.Add(start, count);
                    count = 0;
                }
            }
            if (count >= minBlocks) holes.Add(start, count);
            return holes;
        }

        public void DeallocateMemory(PCB process)
        {
            int blocks = process.AssignedMemoryBlockSize / MinBlockSize;
            int start = process.MemoryBaseAddress / MinBlockSize;
            for (int i = start; i < start + blocks; i++) bitmap[i] = false;
        }

        public int CalculateWaste(IEnumerable<PCB> activeProcesses)
        {
            int waste = 0;
            foreach (var p in activeProcesses)
            {
                if (p.AssignedMemoryBlockSize > 0)
                    waste += (p.AssignedMemoryBlockSize - p.RequiredMemory);
            }
            return waste;
        }
    }
}