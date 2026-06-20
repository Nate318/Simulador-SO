using System.Collections.Generic;

namespace SimuladorSO
{
    public enum ProcessState { New, Ready, Running, Blocked, Terminated }
    public enum Policy { FCFS, SJF, RR, Priority }
    public enum MemoryStrategy { FirstFit, BestFit, WorstFit }

    public class PCB
    {
        public int PID { get; set; }
        public string ProcessName { get; set; }
        public bool IsOSProcess { get; set; }
        public ProcessState State { get; set; }
        public int ProgramCounter { get; set; }
        public int BurstTime { get; set; }
        public int RemainingBurstTime { get; set; }
        public int Priority { get; set; }

        // Memoria
        public int RequiredMemory { get; set; }
        public int AssignedMemoryBlockSize { get; set; }
        public int MemoryBaseAddress { get; set; }

        // E/S
        public Queue<int> InterruptTriggers { get; set; } = new Queue<int>();
        public int CurrentIOTime { get; set; }
        public string ErrorCode { get; set; } = "OK";

        // Estadísticas 
        public int ArrivalTime { get; set; }
        public int StartTime { get; set; } = -1;
        public int FinishTime { get; set; }
        public int TurnAroundTime => FinishTime - ArrivalTime;
        public int WaitingTime => TurnAroundTime - BurstTime;
        public int ResponseTime => StartTime - ArrivalTime;
    }
}