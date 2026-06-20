using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SimuladorSO
{
    public enum ProcessState { NEW, READY, RUNNING, WAITING, TERMINATED, ZOMBIE }
    public enum InterruptType { IO_DISK, IO_NETWORK, IO_GPU, PAGE_FAULT, SYSCALL, SEGFAULT }

    public class Interrupt : IComparable<Interrupt>
    {
        public InterruptType Type { get; set; }
        public int? PID { get; set; }
        public int Priority { get; set; }
        public Dictionary<string, int> Payload { get; set; } = new Dictionary<string, int>();

        public int CompareTo(Interrupt? other)
        {
            if (other == null) return -1;
            return Priority.CompareTo(other.Priority);
        }
    }

    public class Process
    {
        public int PID { get; set; }
        public int? ParentPID { get; set; }
        public List<int> ChildrenPIDs { get; set; } = new List<int>();

        public string Name { get; set; }
        public bool IsSystemProcess { get; set; }

        public int SizeMB { get; set; }
        public int CodeSizeMB { get { return (int)(SizeMB * 0.4); } }
        public int DataSizeMB { get { return (int)(SizeMB * 0.4); } }
        public int ExtraMemoryMB { get { return SizeMB - CodeSizeMB - DataSizeMB; } }

        public ProcessState State { get; set; } = ProcessState.NEW;
        public string ErrorCode { get; set; } = "OK";

        public int Priority { get; set; }
        public int MlfqQueueLevel { get; set; } = 0;
        public double VirtualRuntime { get; set; } = 0;

        public int ProgramCounter { get; set; }
        public Dictionary<string, int> Registers { get; set; } = new Dictionary<string, int> { { "AX", 0 }, { "BX", 0 }, { "CX", 0 }, { "DX", 0 } };

        public Queue<string> Mailbox { get; set; } = new Queue<string>();
        public int MessagesCount { get { return Mailbox.Count; } }

        public int DurationTicks { get; set; }
        public int RemainingTicks { get; set; }

        public int ProgressPercentage
        {
            get
            {
                if (IsSystemProcess || State == ProcessState.ZOMBIE) return 100;
                if (DurationTicks <= 0) return 0;
                double ratio = (DurationTicks - RemainingTicks) / (double)DurationTicks;
                int percent = (int)(ratio * 100);
                if (percent < 0) return 0;
                if (percent > 100) return 100;
                return percent;
            }
        }

        public int? CPU_ID { get; set; }
        public int QuantumUsed { get; set; }
        public int ArrivalTick { get; set; }
        public int WaitingTicks { get; set; }
        public int? FinishTick { get; set; }

        public int TurnaroundTicks
        {
            get
            {
                if (FinishTick.HasValue) return FinishTick.Value - ArrivalTick;
                return 0;
            }
        }

        public int IORemainingTicks { get; set; }
        public string InterruptReason { get; set; }
        public int? PendingFaultPage { get; set; }
        public bool HasError { get; set; }

        public int? MemoryUnitId { get; set; }
        public List<int> AssignedFrames { get; set; } = new List<int>();

        public Process CloneForBenchmark()
        {
            return new Process
            {
                PID = this.PID,
                Name = this.Name,
                IsSystemProcess = this.IsSystemProcess,
                SizeMB = this.SizeMB,
                Priority = this.Priority,
                DurationTicks = this.DurationTicks,
                RemainingTicks = this.DurationTicks,
                ArrivalTick = this.ArrivalTick,
                HasError = false
            };
        }
    }

    public class CPU
    {
        public int Id { get; set; }
        public int NumaNode { get; set; }
        public int ThreadCapacity { get; set; }
        public int ThreadsInUse { get; set; }
        public int ContextSwitchWait { get; set; } = 0;
        public int TotalContextSwitches { get; set; } = 0;
        public Process? CurrentProcess { get; set; }

        public Dictionary<int, int> L1Cache { get; set; } = new Dictionary<int, int>();
        public int L1CacheHits { get; set; } = 0;

        // FEATURE REFINADO: Térmicas Reales y Ventiladores (Active Cooling)
        public double Temperature { get; set; } = 40.0;
        public bool IsThrottling { get; set; } = false;
        public int FanSpeedRPM { get; set; } = 1500; // Ventilador base

        public void Assign(Process p)
        {
            CurrentProcess = p;
            p.CPU_ID = Id;
            p.State = ProcessState.RUNNING;
            p.QuantumUsed = 0;
            p.WaitingTicks = 0;
            ThreadsInUse = ThreadCapacity;
        }

        public void Release()
        {
            if (CurrentProcess != null)
            {
                if (CurrentProcess.State == ProcessState.RUNNING)
                {
                    CurrentProcess.State = ProcessState.READY;
                }
                CurrentProcess.CPU_ID = null;
            }
            CurrentProcess = null;
            ThreadsInUse = 0;
        }
    }

    public class SimulationMetrics
    {
        public int TotalProcesses { get; set; }
        public int CompletedProcesses { get; set; }
        public int TotalTurnaroundTicks { get; set; }
        public int TotalWaitingTicks { get; set; }
        public int GlobalContextSwitches { get; set; }
        public int TotalCpuTicksCount { get; set; }
        public int IdleCpuTicks { get; set; }
        public int SwapOutCount { get; set; }
        public bool IsThrashing { get; set; }
        public int OomKillsCount { get; set; }
        public int NumaPenalties { get; set; }

        public List<int> TurnaroundHistory { get; set; } = new List<int>();

        public double AverageTurnaround
        {
            get { return CompletedProcesses == 0 ? 0 : (double)TotalTurnaroundTicks / CompletedProcesses; }
        }

        public double AverageWaiting
        {
            get { return CompletedProcesses == 0 ? 0 : (double)TotalWaitingTicks / CompletedProcesses; }
        }

        public double CpuUtilization
        {
            get { return TotalCpuTicksCount == 0 ? 0 : 100.0 * (TotalCpuTicksCount - IdleCpuTicks) / TotalCpuTicksCount; }
        }

        public double TurnaroundStdDeviation
        {
            get
            {
                if (TurnaroundHistory.Count < 2) return 0;
                double avg = AverageTurnaround;
                double sum = TurnaroundHistory.Sum(d => Math.Pow(d - avg, 2));
                return Math.Sqrt(sum / (TurnaroundHistory.Count - 1));
            }
        }

        public void RecordCompletion(Process p)
        {
            if (p.IsSystemProcess) return;
            CompletedProcesses++;
            TotalTurnaroundTicks += p.TurnaroundTicks;
            TotalWaitingTicks += p.WaitingTicks;
            TurnaroundHistory.Add(p.TurnaroundTicks);
        }
    }

    public class Frame
    {
        public int Id { get; set; }
        public bool IsFree { get; set; } = true;
        public int? PID { get; set; }
        public int LoadedTick { get; set; }
        public int LastAccessed { get; set; }
        public bool IsReadOnly { get; set; } = false;
    }

    public class TLBEntry { public int PID { get; set; } public int Page { get; set; } public int Frame { get; set; } public int LastAccess { get; set; } }
    public class PageEntry { public int Frame { get; set; } public bool Valid { get; set; } public int LoadedTick { get; set; } public bool Modified { get; set; } }

    public class MMU
    {
        public int UnitId { get; set; }
        public int NumaNode { get; set; }
        public int PageFaults = 0;
        public int PageHits = 0;
        public int TotalRAM_MB { get; private set; }
        public int FrameSizeMB { get; private set; } = 4;

        public List<Frame> PhysicalMemory { get; private set; } = new List<Frame>();
        public List<Frame> SwapSpace { get; private set; } = new List<Frame>();

        public bool TlbEnabled { get; set; } = true;
        public string AllocationAlgorithm { get; set; } = "FirstFit";

        public List<TLBEntry> TLB = new List<TLBEntry>();
        private Dictionary<int, Dictionary<int, PageEntry>> PageTables = new Dictionary<int, Dictionary<int, PageEntry>>();

        public MMU(int id, int totalMb, int numaNode)
        {
            UnitId = id;
            NumaNode = numaNode;
            TotalRAM_MB = totalMb;

            int numFrames = totalMb / FrameSizeMB;
            for (int i = 0; i < numFrames; i++) PhysicalMemory.Add(new Frame { Id = i });
            for (int i = 0; i < numFrames; i++) SwapSpace.Add(new Frame { Id = i });
        }

        public int UsedRAM_MB { get { return PhysicalMemory.Count(f => !f.IsFree) * FrameSizeMB; } }
        public int FreeRAM_MB { get { return TotalRAM_MB - UsedRAM_MB; } }
        public double MemoryUtilization { get { return (double)UsedRAM_MB / TotalRAM_MB * 100; } }
        public int UsedSwap_MB { get { return SwapSpace.Count(f => !f.IsFree) * FrameSizeMB; } }

        public bool Allocate(Process p)
        {
            if (p.PID == 0) return true;

            int requiredFrames = (int)Math.Ceiling((double)p.SizeMB / FrameSizeMB);
            List<Frame> selectedFrames = new List<Frame>();

            if (AllocationAlgorithm == "BestFit")
            {
                var freeFrames = PhysicalMemory.Where(f => f.IsFree).ToList();
                if (freeFrames.Count >= requiredFrames) selectedFrames = freeFrames.Take(requiredFrames).ToList();
            }
            else
            {
                foreach (var frame in PhysicalMemory)
                {
                    if (frame.IsFree)
                    {
                        selectedFrames.Add(frame);
                        if (selectedFrames.Count == requiredFrames) break;
                    }
                }
            }

            if (selectedFrames.Count < requiredFrames) return false;

            PageTables[p.PID] = new Dictionary<int, PageEntry>();
            int frameCounter = 0;

            foreach (var frame in selectedFrames)
            {
                frame.IsFree = false;
                frame.PID = p.PID;
                if (frameCounter < (requiredFrames * 0.3)) frame.IsReadOnly = true;
                else frame.IsReadOnly = false;
                p.AssignedFrames.Add(frame.Id);
                PageTables[p.PID][frame.Id] = new PageEntry { Frame = frame.Id, Valid = true, LoadedTick = 0, Modified = false };
                frameCounter++;
            }
            return true;
        }

        public void Deallocate(Process p)
        {
            foreach (var frameId in p.AssignedFrames)
            {
                if (frameId < PhysicalMemory.Count)
                {
                    PhysicalMemory[frameId].IsFree = true;
                    PhysicalMemory[frameId].PID = null;
                    PhysicalMemory[frameId].IsReadOnly = false;
                }
            }

            foreach (var frame in SwapSpace) { if (frame.PID == p.PID) { frame.IsFree = true; frame.PID = null; } }
            p.AssignedFrames.Clear();

            var tlbToRemove = TLB.Where(entry => entry.PID == p.PID).ToList();
            foreach (var entry in tlbToRemove) TLB.Remove(entry);
            PageTables.Remove(p.PID);
        }

        public int? Translate(int pid, int page, int tick, bool isWriteOperation = false)
        {
            foreach (var entry in TLB)
            {
                if (entry.PID == pid && entry.Page == page)
                {
                    PageHits++;
                    entry.LastAccess = tick;
                    PhysicalMemory[entry.Frame].LastAccessed = tick;
                    if (isWriteOperation && PhysicalMemory[entry.Frame].IsReadOnly) return -999;
                    if (isWriteOperation && PageTables.ContainsKey(pid)) PageTables[pid][page].Modified = true;
                    return entry.Frame;
                }
            }

            if (PageTables.ContainsKey(pid) && PageTables[pid].ContainsKey(page))
            {
                var ptEntry = PageTables[pid][page];
                if (ptEntry.Valid)
                {
                    PageHits++;
                    UpdateTLB(pid, page, ptEntry.Frame, tick);
                    PhysicalMemory[ptEntry.Frame].LastAccessed = tick;
                    if (isWriteOperation && PhysicalMemory[ptEntry.Frame].IsReadOnly) return -999;
                    if (isWriteOperation) ptEntry.Modified = true;
                    return ptEntry.Frame;
                }
            }
            PageFaults++;
            return null;
        }

        public void UpdateTLB(int pid, int page, int frame, int tick)
        {
            if (!TlbEnabled) return;
            if (TLB.Count >= 16)
            {
                TLBEntry oldest = TLB[0];
                foreach (var entry in TLB) { if (entry.LastAccess < oldest.LastAccess) oldest = entry; }
                TLB.Remove(oldest);
            }
            TLB.Add(new TLBEntry { PID = pid, Page = page, Frame = frame, LastAccess = tick });
        }

        public int HandlePageFault(int pid, int page, int currentTick)
        {
            if (!PageTables.ContainsKey(pid)) PageTables[pid] = new Dictionary<int, PageEntry>();

            int swapPenalty = 0;
            Frame targetFrame = PhysicalMemory.FirstOrDefault(f => f.IsFree);

            if (targetFrame == null)
            {
                List<Frame> usedFrames = PhysicalMemory.Where(f => f.PID.HasValue && f.PID.Value > 3).ToList();
                if (usedFrames.Count == 0) return -1;

                targetFrame = usedFrames.OrderBy(f => f.LoadedTick).First();

                if (targetFrame.PID.HasValue && PageTables.ContainsKey(targetFrame.PID.Value))
                {
                    var oldEntries = PageTables[targetFrame.PID.Value];
                    foreach (var kvp in oldEntries)
                    {
                        if (kvp.Value.Frame == targetFrame.Id)
                        {
                            kvp.Value.Valid = false;
                            Frame swapSlot = SwapSpace.FirstOrDefault(s => s.IsFree);
                            if (swapSlot != null) { swapSlot.IsFree = false; swapSlot.PID = targetFrame.PID.Value; } else return -1;
                            if (kvp.Value.Modified) swapPenalty = 10;

                            var tlbToRemove = TLB.Where(item => item.PID == targetFrame.PID.Value && item.Frame == targetFrame.Id).ToList();
                            foreach (var item in tlbToRemove) TLB.Remove(item);
                            break;
                        }
                    }
                }
            }

            targetFrame.IsFree = false; targetFrame.PID = pid; targetFrame.LoadedTick = currentTick; targetFrame.LastAccessed = currentTick;
            PageTables[pid][page] = new PageEntry { Frame = targetFrame.Id, Valid = true, LoadedTick = currentTick, Modified = false };

            foreach (var s in SwapSpace) { if (s.PID == pid) { s.IsFree = true; s.PID = null; } }

            return swapPenalty;
        }

        public void MapSharedMemory(int pid1, int pid2)
        {
            if (PageTables.ContainsKey(pid1) && PageTables.ContainsKey(pid2))
            {
                var pt1 = PageTables[pid1].Values.FirstOrDefault(e => e.Valid);
                if (pt1 != null)
                {
                    int sharedFrameId = pt1.Frame;
                    PageTables[pid2][99] = new PageEntry { Frame = sharedFrameId, Valid = true, LoadedTick = 0, Modified = false };
                }
            }
        }
    }

    public abstract class Scheduler
    {
        public List<Process> ReadyQueue { get; set; } = new List<Process>();
        public int Quantum { get; set; } = 4;
        public virtual void AddProcess(Process p) { p.State = ProcessState.READY; ReadyQueue.Add(p); }
        public abstract Process? GetNextProcess(int currentTick);
        public virtual Process? CheckPreemption(Process current) { return null; }
        public virtual void ApplyAging() { }
    }

    public class FCFS_Scheduler : Scheduler
    {
        public FCFS_Scheduler() { Quantum = 999999; }
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            var p = ReadyQueue.OrderBy(x => x.ArrivalTick).ThenBy(x => x.PID).First();
            ReadyQueue.Remove(p); return p;
        }
    }

    public class SJF_Scheduler : Scheduler
    {
        public SJF_Scheduler() { Quantum = 999999; }
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            var p = ReadyQueue.OrderBy(x => x.DurationTicks).ThenBy(x => x.PID).First();
            ReadyQueue.Remove(p); return p;
        }
    }

    public class MLFQ_Scheduler : Scheduler
    {
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;

            Process bestProcess = ReadyQueue.OrderBy(p => p.MlfqQueueLevel).ThenBy(p => p.ArrivalTick).First();
            ReadyQueue.Remove(bestProcess);

            if (bestProcess.MlfqQueueLevel == 0) Quantum = 2;
            else if (bestProcess.MlfqQueueLevel == 1) Quantum = 4;
            else Quantum = 999;

            return bestProcess;
        }

        public override Process? CheckPreemption(Process current)
        {
            if (ReadyQueue.Count == 0) return null;
            Process bestProcess = ReadyQueue.OrderBy(p => p.MlfqQueueLevel).First();
            if (bestProcess.MlfqQueueLevel < current.MlfqQueueLevel) return bestProcess;
            return null;
        }

        public override void ApplyAging()
        {
            foreach (var p in ReadyQueue) { if (p.WaitingTicks > 30 && p.MlfqQueueLevel > 0) { p.MlfqQueueLevel--; p.WaitingTicks = 0; } }
        }
    }

    public class CFS_Scheduler : Scheduler
    {
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            Process bestProcess = ReadyQueue.OrderBy(p => p.VirtualRuntime).First();
            ReadyQueue.Remove(bestProcess); return bestProcess;
        }

        public override Process? CheckPreemption(Process current)
        {
            if (ReadyQueue.Count == 0) return null;
            Process bestProcess = ReadyQueue.OrderBy(p => p.VirtualRuntime).First();
            if (bestProcess.VirtualRuntime < current.VirtualRuntime - 5) return bestProcess;
            return null;
        }
    }

    public class RR_Scheduler : Scheduler
    {
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            var p = ReadyQueue[0]; ReadyQueue.RemoveAt(0); return p;
        }
    }

    public class SimulationEngine
    {
        public int TickCount { get; private set; } = 0;
        public SimulationMetrics Metrics { get; private set; } = new SimulationMetrics();

        public List<CPU> CPUs { get; set; } = new List<CPU>();
        public List<Scheduler> Schedulers { get; set; } = new List<Scheduler>();
        public Dictionary<int, string> SchedulerNames { get; set; } = new Dictionary<int, string>();

        public List<Process> ProcessTable { get; set; } = new List<Process>();
        public Queue<Process> JobQueue { get; set; } = new Queue<Process>();

        public List<Process> WaitQueue_Disk { get; set; } = new List<Process>();
        public List<Process> WaitQueue_Net { get; set; } = new List<Process>();
        public List<Process> WaitQueue_Sys { get; set; } = new List<Process>();

        private List<Interrupt> InterruptQueue { get; set; } = new List<Interrupt>();
        public List<MMU> MemoryUnits { get; set; } = new List<MMU>();

        public bool AutoCreateProcesses { get; set; } = false;
        private Random rnd = new Random();
        public List<string> ConsoleLog { get; set; } = new List<string>();

        public bool IsBooting { get; set; } = true;
        public int BootSequenceWait { get; set; } = 15;

        public SimulationEngine(int cpus, int threads, bool isBenchmark = false)
        {
            MemoryUnits.Add(new MMU(0, 4096, 0));

            for (int i = 0; i < cpus; i++)
            {
                CPUs.Add(new CPU { Id = i, ThreadCapacity = threads, NumaNode = 0 });
                SetCpuScheduler(i, "MLFQ", 4);
            }

            if (!isBenchmark)
            {
                InjectManualProcess(128, -1, 0, true, "Kernel_Core");
                InjectManualProcess(32, -1, 1, true, "Systemd_Init");
                InjectManualProcess(16, -1, 1, true, "KSwapd");
                Log("[KERNEL] Sistema encendido. Ejecutando Boot Sequence...");
            }
            else
            {
                IsBooting = false;
            }
        }

        public void SetCpuScheduler(int cpuId, string algName, int quantum = 4)
        {
            Scheduler s;
            if (algName == "MLFQ") s = new MLFQ_Scheduler();
            else if (algName == "CFS") s = new CFS_Scheduler();
            else if (algName == "FCFS") s = new FCFS_Scheduler();
            else if (algName == "SJF") s = new SJF_Scheduler();
            else s = new RR_Scheduler { Quantum = quantum };

            if (cpuId < Schedulers.Count) Schedulers[cpuId] = s;
            else Schedulers.Add(s);

            SchedulerNames[cpuId] = algName;
        }

        public void Log(string msg) { ConsoleLog.Add($"[{TickCount}] {msg}"); }

        public void ForkProcess(int parentPid)
        {
            Process parent = ProcessTable.FirstOrDefault(x => x.PID == parentPid);
            if (parent != null && !parent.IsSystemProcess)
            {
                var child = InjectManualProcess(parent.SizeMB, parent.DurationTicks, parent.Priority);
                child.ParentPID = parent.PID;
                child.Name = $"{parent.Name}_hijo";
                parent.ChildrenPIDs.Add(child.PID);
                Log($"[SYSCALL] PID {parent.PID} ejecutó fork(). Creado PID {child.PID}");
            }
        }

        public void SendIPC(int senderPid, int receiverPid, string message)
        {
            Process receiver = ProcessTable.FirstOrDefault(x => x.PID == receiverPid);
            if (receiver != null && receiver.State != ProcessState.TERMINATED)
            {
                receiver.Mailbox.Enqueue($"De {senderPid}: {message}");
                if (receiver.State == ProcessState.WAITING && receiver.InterruptReason == "IPC_WAIT") receiver.IORemainingTicks = 0;
            }
        }

        public void CreateSharedMemory(int pid1, int pid2)
        {
            MemoryUnits[0].MapSharedMemory(pid1, pid2);
        }

        public void KillProcess(int pid, bool isCascade = false)
        {
            Process p = ProcessTable.FirstOrDefault(x => x.PID == pid);
            if (p != null && !p.IsSystemProcess && p.State != ProcessState.TERMINATED)
            {
                p.State = ProcessState.TERMINATED; p.ErrorCode = isCascade ? "KILLED_CASCADE" : "SIGKILL";
                if (p.MemoryUnitId.HasValue) MemoryUnits[p.MemoryUnitId.Value].Deallocate(p);
                foreach (var cpu in CPUs) { if (cpu.CurrentProcess != null && cpu.CurrentProcess.PID == pid) cpu.Release(); }
                var childrenCopy = new List<int>(p.ChildrenPIDs);
                foreach (var childId in childrenCopy) KillProcess(childId, true);
            }
        }

        private void TriggerOomKiller()
        {
            Process victim = null; int maxScore = -1;

            foreach (var p in ProcessTable)
            {
                if (!p.IsSystemProcess && p.State != ProcessState.TERMINATED && p.State != ProcessState.ZOMBIE)
                {
                    int score = p.SizeMB + (10 - p.Priority) * 10;
                    if (score > maxScore) { maxScore = score; victim = p; }
                }
            }

            if (victim != null)
            {
                Metrics.OomKillsCount++;
                Log($"[OOM-KILLER] RAM Llenas. OOM_KILLED ejecutado sobre PID {victim.PID}.");
                KillProcess(victim.PID, false);
            }
        }

        public void Tick()
        {
            TickCount++;

            if (IsBooting)
            {
                BootSequenceWait--;
                if (BootSequenceWait <= 0) { IsBooting = false; Log("[KERNEL] Boot completado."); }
            }
            else
            {
                if (AutoCreateProcesses && rnd.Next(1, 100) < 5) InjectManualProcess(rnd.Next(32, 256), rnd.Next(30, 150), rnd.Next(1, 10));
                AdmitNewProcesses();
            }

            if (TickCount % 50 == 0)
            {
                var zombies = ProcessTable.Where(p => p.State == ProcessState.ZOMBIE).ToList();
                foreach (var z in zombies) { z.State = ProcessState.TERMINATED; Log($"[KERNEL] Zombie PID {z.PID} limpiado (Reaped)."); }
            }

            ProcessInterrupts(); UpdateWaitQueues(); WorkStealing(); ExecuteCPUs(); Dispatch();
            foreach (var s in Schedulers) { s.ApplyAging(); foreach (var p in s.ReadyQueue) p.WaitingTicks++; }
        }

        private void WorkStealing()
        {
            for (int i = 0; i < CPUs.Count; i++)
            {
                if (CPUs[i].CurrentProcess == null && Schedulers[i].ReadyQueue.Count == 0)
                {
                    Scheduler richestScheduler = Schedulers.OrderByDescending(s => s.ReadyQueue.Count).FirstOrDefault();
                    if (richestScheduler != null && richestScheduler.ReadyQueue.Count > 1 && richestScheduler != Schedulers[i])
                    {
                        Process stolenProcess = richestScheduler.ReadyQueue.Last();
                        richestScheduler.ReadyQueue.Remove(stolenProcess);
                        Schedulers[i].ReadyQueue.Add(stolenProcess);
                    }
                }
            }
        }

        private void AdmitNewProcesses()
        {
            while (JobQueue.Count > 0)
            {
                var p = JobQueue.Peek();
                var unit = MemoryUnits[0];

                if (unit.Allocate(p))
                {
                    JobQueue.Dequeue();
                    p.MemoryUnitId = unit.UnitId;

                    int targetIndex = 0; int minLoad = 9999;
                    for (int i = 0; i < Schedulers.Count; i++)
                    {
                        if (Schedulers[i].ReadyQueue.Count < minLoad) { minLoad = Schedulers[i].ReadyQueue.Count; targetIndex = i; }
                    }
                    Schedulers[targetIndex].AddProcess(p);
                }
                else break;
            }
        }

        private void ExecuteCPUs()
        {
            foreach (var cpu in CPUs)
            {
                if (cpu.ContextSwitchWait > 0) { cpu.ContextSwitchWait--; continue; }

                Metrics.TotalCpuTicksCount++;
                var p = cpu.CurrentProcess;

                if (p == null)
                {
                    Metrics.IdleCpuTicks++;

                    // LÓGICA TÉRMICA CORREGIDA (Enfriamiento)
                    cpu.FanSpeedRPM = Math.Max(1000, cpu.FanSpeedRPM - 50); // El ventilador se desacelera
                    double coolingRate = 0.01 * (cpu.FanSpeedRPM / 1000.0);
                    cpu.Temperature = Math.Max(35.0, cpu.Temperature - coolingRate);

                    if (cpu.Temperature < 65.0) cpu.IsThrottling = false; // Histéresis: Libera el estrangulamiento al llegar a 65
                    continue;
                }

                // LÓGICA TÉRMICA CORREGIDA (Calentamiento Realista Lento)
                cpu.Temperature += 0.008; // Incremento súper lento por Tick de carga

                // Activar Ventiladores Virtuales
                if (cpu.Temperature > 70.0) cpu.FanSpeedRPM = Math.Min(5000, cpu.FanSpeedRPM + 100);
                else cpu.FanSpeedRPM = Math.Max(1500, cpu.FanSpeedRPM - 10);

                if (cpu.Temperature >= 85.0) cpu.IsThrottling = true; // Se ahoga a 85
                if (cpu.Temperature >= 95.0) cpu.Temperature = 95.0; // Max Físico

                int execPower = cpu.IsThrottling ? Math.Max(1, cpu.ThreadCapacity / 2) : cpu.ThreadCapacity;

                if (p.MemoryUnitId.HasValue && p.MemoryUnitId.Value != cpu.NumaNode)
                {
                    execPower = Math.Max(1, execPower - 1);
                    Metrics.NumaPenalties++;
                }

                if (p.DurationTicks != -1)
                {
                    p.RemainingTicks = Math.Max(0, p.RemainingTicks - execPower);
                    p.VirtualRuntime += (10.0 / Math.Max(1, p.Priority));
                }

                p.ProgramCounter += rnd.Next(4, 16);
                p.QuantumUsed++;

                if (!p.IsSystemProcess && rnd.NextDouble() < 0.1 && p.MemoryUnitId.HasValue)
                {
                    var unit = MemoryUnits[p.MemoryUnitId.Value];
                    int reqPage = rnd.Next(0, Math.Max(1, (int)Math.Ceiling((double)p.SizeMB / unit.FrameSizeMB)));
                    bool isWrite = rnd.NextDouble() < 0.3;

                    if (cpu.L1Cache.ContainsKey(reqPage)) cpu.L1CacheHits++;
                    else
                    {
                        int? translatedFrame = unit.Translate(p.PID, reqPage, TickCount, isWrite);

                        if (translatedFrame == -999) { InterruptQueue.Add(new Interrupt { Type = InterruptType.SEGFAULT, PID = p.PID, Priority = 0 }); cpu.Release(); continue; }
                        else if (translatedFrame == null) { InterruptQueue.Add(new Interrupt { Type = InterruptType.PAGE_FAULT, PID = p.PID, Priority = 1, Payload = new Dictionary<string, int> { { "Page", reqPage }, { "Duration", 10 } } }); cpu.Release(); continue; }
                        else { if (cpu.L1Cache.Count >= 4) cpu.L1Cache.Clear(); cpu.L1Cache[reqPage] = translatedFrame.Value; }
                    }
                }

                if (!p.IsSystemProcess && rnd.NextDouble() < 0.02)
                {
                    int randType = rnd.Next(0, 3);
                    InterruptType type = InterruptType.IO_DISK;
                    if (randType == 1) type = InterruptType.IO_NETWORK;
                    if (randType == 2) type = InterruptType.IO_GPU;
                    InterruptQueue.Add(new Interrupt { Type = type, PID = p.PID, Priority = 3, Payload = new Dictionary<string, int> { { "Duration", 10 } } });
                    cpu.Release(); continue;
                }

                if (p.DurationTicks != -1 && p.RemainingTicks <= 0) { Terminate(p, "OK"); cpu.Release(); continue; }

                var sched = Schedulers[cpu.Id];
                bool quantumExpired = p.QuantumUsed >= sched.Quantum;

                if (quantumExpired)
                {
                    Metrics.GlobalContextSwitches++; cpu.TotalContextSwitches++; cpu.ContextSwitchWait = 1; cpu.L1Cache.Clear();
                    if (sched is MLFQ_Scheduler && p.MlfqQueueLevel < 2) p.MlfqQueueLevel++;
                    sched.AddProcess(p); cpu.Release();
                }
                else
                {
                    var preempter = sched.CheckPreemption(p);
                    if (preempter != null)
                    {
                        Metrics.GlobalContextSwitches++; cpu.TotalContextSwitches++; cpu.ContextSwitchWait = 1; cpu.L1Cache.Clear();
                        sched.ReadyQueue.Remove(preempter); sched.AddProcess(p); cpu.Assign(preempter);
                    }
                }
            }
        }

        private void Dispatch()
        {
            foreach (var cpu in CPUs) { if (cpu.CurrentProcess == null && cpu.ContextSwitchWait == 0) { var p = Schedulers[cpu.Id].GetNextProcess(TickCount); if (p != null) { cpu.Assign(p); } } }
        }

        private void ProcessInterrupts()
        {
            if (InterruptQueue.Count == 0) return;
            InterruptQueue.Sort(); var current = InterruptQueue[0]; InterruptQueue.RemoveAt(0);

            Process p = ProcessTable.FirstOrDefault(x => x.PID == current.PID);
            if (p != null && p.State != ProcessState.TERMINATED)
            {
                if (current.Type == InterruptType.SEGFAULT) { Terminate(p, "SEGFAULT_ACCESS_VIOLATION"); return; }

                p.State = ProcessState.WAITING; p.IORemainingTicks = current.Payload.ContainsKey("Duration") ? current.Payload["Duration"] : 10;

                if (current.Type == InterruptType.PAGE_FAULT) { p.InterruptReason = $"Fallo Pág"; p.PendingFaultPage = current.Payload["Page"]; WaitQueue_Sys.Add(p); }
                else if (current.Type == InterruptType.SYSCALL) { p.InterruptReason = "SYSCALL"; WaitQueue_Sys.Add(p); }
                else if (current.Type == InterruptType.IO_DISK) { p.InterruptReason = "E/S Disco"; WaitQueue_Disk.Add(p); }
                else if (current.Type == InterruptType.IO_NETWORK) { p.InterruptReason = "E/S Red"; WaitQueue_Net.Add(p); }
                else if (current.Type == InterruptType.IO_GPU) { p.InterruptReason = "E/S Render GPU"; WaitQueue_Disk.Add(p); }
            }
        }

        private void UpdateWaitQueues()
        {
            for (int i = WaitQueue_Sys.Count - 1; i >= 0; i--)
            {
                var p = WaitQueue_Sys[i]; p.IORemainingTicks--;
                if (p.IORemainingTicks <= 0)
                {
                    if (p.PendingFaultPage.HasValue && p.MemoryUnitId.HasValue)
                    {
                        int swapPenalty = MemoryUnits[p.MemoryUnitId.Value].HandlePageFault(p.PID, p.PendingFaultPage.Value, TickCount);
                        if (swapPenalty > 0) { p.IORemainingTicks += swapPenalty; p.InterruptReason = "Swap Out a Disco"; Metrics.SwapOutCount++; Metrics.IsThrashing = (Metrics.SwapOutCount > 20); continue; }
                        else if (swapPenalty == -1) { TriggerOomKiller(); }
                        p.PendingFaultPage = null;
                    }
                    WaitQueue_Sys.RemoveAt(i); Schedulers[p.CPU_ID ?? 0].AddProcess(p);
                }
            }

            for (int i = WaitQueue_Disk.Count - 1; i >= 0; i--) { var p = WaitQueue_Disk[i]; p.IORemainingTicks--; if (p.IORemainingTicks <= 0) { WaitQueue_Disk.RemoveAt(i); Schedulers[p.CPU_ID ?? 0].AddProcess(p); } }
            for (int i = WaitQueue_Net.Count - 1; i >= 0; i--) { var p = WaitQueue_Net[i]; p.IORemainingTicks--; if (p.IORemainingTicks <= 0) { WaitQueue_Net.RemoveAt(i); Schedulers[p.CPU_ID ?? 0].AddProcess(p); } }
        }

        private void Terminate(Process p, string code)
        {
            p.State = ProcessState.ZOMBIE; p.ErrorCode = code; p.FinishTick = TickCount;
            if (p.MemoryUnitId.HasValue) MemoryUnits[p.MemoryUnitId.Value].Deallocate(p);
            Metrics.RecordCompletion(p);
        }

        public Process InjectManualProcess(int sizeMB, int burst, int priority, bool isSystem = false, string sysName = null)
        {
            if (!isSystem) Metrics.TotalProcesses++;
            var p = new Process { PID = ProcessTable.Count + 1, Name = isSystem ? sysName : $"App_{ProcessTable.Count + 1}", IsSystemProcess = isSystem, SizeMB = sizeMB, DurationTicks = burst, RemainingTicks = burst, Priority = priority, ArrivalTick = TickCount, HasError = false };
            ProcessTable.Add(p); JobQueue.Enqueue(p); return p;
        }
    }

    public class BenchmarkResult
    {
        public string Algoritmo { get; set; }
        public int TicksTotales { get; set; }
        public double TurnaroundMedio { get; set; }
        public double VarianzaPredictibilidad { get; set; }
    }

    public class BenchmarkEngine
    {
        public static void RunBenchmarkAsync(int numProcesses, Action<double, string> onProgressUpdate, Action<List<BenchmarkResult>> onComplete)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                var results = new List<BenchmarkResult>();
                string[] algorithms = { "FCFS", "SJF", "RR", "MLFQ" };

                List<Process> masterProcessList = new List<Process>();
                Random fixedSeedRnd = new Random(42);
                for (int i = 0; i < numProcesses; i++)
                {
                    masterProcessList.Add(new Process
                    {
                        PID = i + 3,
                        Name = $"BenchApp_{i}",
                        IsSystemProcess = false,
                        SizeMB = 16,
                        Priority = fixedSeedRnd.Next(1, 5),
                        DurationTicks = fixedSeedRnd.Next(10, 80),
                        RemainingTicks = 0,
                        ArrivalTick = 0,
                        HasError = false
                    });
                }

                foreach (var alg in algorithms)
                {
                    onProgressUpdate(0, $"Simulando {alg}...");
                    var engine = new SimulationEngine(1, 1, true);
                    engine.SetCpuScheduler(0, alg, 4);

                    foreach (var p in masterProcessList)
                    {
                        var clone = p.CloneForBenchmark();
                        engine.ProcessTable.Add(clone);
                        engine.JobQueue.Enqueue(clone);
                        engine.Metrics.TotalProcesses++;
                    }

                    int totalToFinish = numProcesses;
                    int finished = 0;

                    while (finished < totalToFinish)
                    {
                        engine.Tick();

                        if (engine.TickCount % 100 == 0)
                        {
                            finished = engine.ProcessTable.Count(p => p.State == ProcessState.TERMINATED && !p.IsSystemProcess);
                            double progress = ((double)finished / totalToFinish) * 100;
                            onProgressUpdate(progress, $"Simulando {alg} (Tick: {engine.TickCount})...");
                        }

                        if (engine.TickCount > 300000) break;
                    }

                    results.Add(new BenchmarkResult
                    {
                        Algoritmo = alg,
                        TicksTotales = engine.TickCount,
                        TurnaroundMedio = Math.Round(engine.Metrics.AverageTurnaround, 2),
                        VarianzaPredictibilidad = Math.Round(engine.Metrics.TurnaroundStdDeviation, 2)
                    });
                }

                onComplete(results);
            });
        }
    }
}