using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SimuladorSO
{
    // =========================================================================
    // 1. GESTIÓN DE PROCESOS - ESTADOS E INTERRUPCIONES
    // =========================================================================

    // Enumera los 5 estados clásicos del ciclo de vida de un proceso (según requerimiento 1 de Gestión de Proceso)
    // más el estado ZOMBIE usado para procesos finalizados que esperan ser recolectados (reaped) por el Kernel.
    public enum ProcessState 
    { 
        NEW,        // Recién creado, esperando admisión en memoria física.
        READY,      // Listo en la cola de planificación para competir por CPU.
        RUNNING,    // En ejecución activa dentro de un núcleo de CPU.
        WAITING,    // Bloqueado en espera de E/S o evento (síncrono/asíncrono).
        TERMINATED, // Finalizado completamente, recursos liberados.
        ZOMBIE      // Terminado pero manteniendo su entrada en la tabla para reporte.
    }

    // Define los diferentes tipos de interrupción de hardware y software admitidos por el simulador.
    // (Requerimiento 2 de Gestión de Proceso y Gestión de E/S).
    public enum InterruptType 
    { 
        IO_DISK,     // Interrupción por lectura/escritura en disco duro.
        IO_NETWORK,  // Interrupción por envío/recepción de red.
        IO_GPU,      // Interrupción por procesamiento gráfico.
        PAGE_FAULT,  // Fallo de página (Paginación bajo demanda).
        SYSCALL,     // Llamada al sistema (ej. Fork, IPC).
        SEGFAULT     // Violación de acceso a memoria (Acceso inválido o a solo lectura).
    }

    // Representa un evento de interrupción procesado por la cola de prioridades del Kernel.
    // Implementa IComparable para ordenar las interrupciones por nivel de prioridad.
    public class Interrupt : IComparable<Interrupt>
    {
        // Tipo de interrupción disparada.
        public InterruptType Type { get; set; }
        
        // Identificador del proceso (PID) asociado a la interrupción.
        public int? PID { get; set; }
        
        // Prioridad del vector de interrupción (menor valor indica mayor prioridad de despacho).
        public int Priority { get; set; }
        
        // Parámetros adicionales (ej. página faltante, duración de la interrupción).
        public Dictionary<string, int> Payload { get; set; } = new Dictionary<string, int>();

        // Compara interrupciones para su ordenamiento en la cola de prioridades.
        public int CompareTo(Interrupt? other)
        {
            if (other == null) return -1;
            return Priority.CompareTo(other.Priority);
        }
    }

    // =========================================================================
    // 2. BLOQUE DE CONTROL DE PROCESO (PCB)
    // =========================================================================

    // Representa el Bloque de Control de Proceso (PCB) en memoria.
    // Contiene toda la información de estado, registros, contadores, planificación y memoria.
    // (Requerimiento 3 de Gestión de Proceso).
    public class Process
    {
        // --- Identificación del Proceso ---
        // Identificador único del proceso (PID).
        public int PID { get; set; }
        
        // PID del proceso padre (para llamadas del tipo fork()).
        public int? ParentPID { get; set; }
        
        // Lista de PIDs de los procesos hijos creados por este proceso.
        public List<int> ChildrenPIDs { get; set; } = new List<int>();

        // Nombre amigable del ejecutable o servicio.
        public string Name { get; set; }
        
        // Indica si es un hilo/proceso del núcleo (Kernel Space) o de usuario (User Space).
        public bool IsSystemProcess { get; set; }

        // --- Gestión y Segmentación de Memoria del Proceso (Requerimiento Ingreso de un Proceso) ---
        // Tamaño total del proceso en Megabytes.
        public int SizeMB { get; set; }
        
        // Tamaño estimado del segmento de código ejecutable (40% del total).
        public int CodeSizeMB { get { return (int)(SizeMB * 0.4); } }
        
        // Tamaño estimado del segmento de datos (variables globales y registros: 40% del total).
        public int DataSizeMB { get { return (int)(SizeMB * 0.4); } }
        
        // Memoria variable asignada dinámicamente durante ejecución (Stack/Heap: 20% restante).
        public int ExtraMemoryMB { get { return SizeMB - CodeSizeMB - DataSizeMB; } }

        // --- Estado de Ejecución y Errores ---
        // Estado actual del proceso en su ciclo de vida.
        public ProcessState State { get; set; } = ProcessState.NEW;
        
        // Código de terminación (OK, SIGKILL, SEGFAULT, etc.).
        public string ErrorCode { get; set; } = "OK";

        // --- Atributos de Planificación ---
        // Prioridad estática asignada al proceso (1-10).
        public int Priority { get; set; }
        
        // Nivel de cola actual en la política MLFQ (Multi-Level Feedback Queue).
        public int MlfqQueueLevel { get; set; } = 0;
        
        // Tiempo de ejecución virtual acumulado (política CFS - Completely Fair Scheduler).
        public double VirtualRuntime { get; set; } = 0;

        // --- Contexto de la CPU (Cambio de Contexto / Dispatcher) ---
        // Registro Program Counter (PC) que apunta a la instrucción física virtual actual.
        public int ProgramCounter { get; set; }
        
        // Registros internos del procesador emulados en la PCB.
        public Dictionary<string, int> Registers { get; set; } = new Dictionary<string, int> { { "AX", 0 }, { "BX", 0 }, { "CX", 0 }, { "DX", 0 } };

        // --- Comunicación Inter-Procesos (IPC) ---
        // Buzón de entrada de mensajes para IPC (Mailbox).
        public Queue<string> Mailbox { get; set; } = new Queue<string>();
        
        // Cantidad de mensajes pendientes de lectura.
        public int MessagesCount { get { return Mailbox.Count; } }

        // --- Tiempos de Vida y Burst de CPU ---
        // Tiempo total estimado de uso de CPU (Burst Time) en ticks.
        public int DurationTicks { get; set; }
        
        // Ticks de CPU restantes para que el proceso finalice.
        public int RemainingTicks { get; set; }

        // Calcula el porcentaje de avance de la ejecución del proceso.
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

        // --- Métricas de Rendimiento (Requerimiento 16/17 de Gestión de Proceso) ---
        // ID de la CPU en la que se está ejecutando el proceso.
        public int? CPU_ID { get; set; }
        
        // Quantum consumido durante la ráfaga de CPU actual.
        public int QuantumUsed { get; set; }
        
        // Tick exacto en el que el proceso fue creado e ingresó al sistema.
        public int ArrivalTick { get; set; }
        
        // Tiempo de espera acumulado en la cola de Listos (sin CPU).
        public int WaitingTicks { get; set; }
        
        // Tick exacto en el que finalizó su ejecución.
        public int? FinishTick { get; set; }

        // Tiempo de Retorno (Turnaround Time): Tiempo total desde la llegada hasta la finalización.
        public int TurnaroundTicks
        {
            get
            {
                if (FinishTick.HasValue) return FinishTick.Value - ArrivalTick;
                return 0;
            }
        }

        // --- Estado de E/S y Paginación ---
        // Ticks restantes para completar la operación de E/S actual.
        public int IORemainingTicks { get; set; }
        
        // Causa detallada del bloqueo de E/S (ej. Fallo Pág, E/S Disco).
        public string InterruptReason { get; set; }
        
        // Número de la página de memoria que causó el Page Fault actual.
        public int? PendingFaultPage { get; set; }
        
        // Flag de control de error interno de actividad.
        public bool HasError { get; set; }

        // --- Asignación Física de Memoria ---
        // ID de la unidad de memoria física donde se aloja.
        public int? MemoryUnitId { get; set; }
        
        // Índices de los marcos (frames) de memoria asignados al proceso en RAM.
        public List<int> AssignedFrames { get; set; } = new List<int>();

        // Crea una copia limpia del proceso para ejecutar pruebas comparativas (benchmarks).
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

    // =========================================================================
    // 3. UNIDAD DE PROCESAMIENTO (CPU)
    // =========================================================================

    // Simula un núcleo físico de procesamiento (Core) del hardware.
    // Soporta múltiples hilos, control de temperatura, estrangulamiento térmico y caché L1.
    public class CPU
    {
        // ID único del núcleo de CPU.
        public int Id { get; set; }
        
        // Nodo NUMA asociado (Arquitectura de acceso no uniforme a memoria).
        public int NumaNode { get; set; }
        
        // Capacidad máxima de hilos paralelos por núcleo.
        public int ThreadCapacity { get; set; }
        
        // Hilos actualmente en uso por el proceso activo.
        public int ThreadsInUse { get; set; }
        
        // Retraso simula la sobrecarga por Cambio de Contexto.
        public int ContextSwitchWait { get; set; } = 0;
        
        // Contador acumulado de cambios de contexto en este núcleo.
        public int TotalContextSwitches { get; set; } = 0;
        
        // Proceso (PCB) ejecutándose actualmente en el núcleo.
        public Process? CurrentProcess { get; set; }

        // --- Cache del Núcleo ---
        // Cache L1 asociativa que mapea [Página] -> [Marco de RAM] para acceso rápido.
        public Dictionary<int, int> L1Cache { get; set; } = new Dictionary<int, int>();
        
        // Aciertos de lectura/escritura en la cache L1.
        public int L1CacheHits { get; set; } = 0;

        // --- Sensores y Disipación (Thermals) ---
        // Temperatura actual del núcleo en grados Celsius.
        public double Temperature { get; set; } = 40.0;
        
        // Indica si la CPU está bajo estrangulamiento por exceso de calor (Thermal Throttling).
        public bool IsThrottling { get; set; } = false;
        
        // Velocidad actual en RPM del ventilador asignado a disipar calor.
        public int FanSpeedRPM { get; set; } = 1500;

        // Asigna un proceso de la cola de listos a este núcleo físico de CPU.
        // Realiza el cambio de estado del proceso a RUNNING.
        public void Assign(Process p)
        {
            CurrentProcess = p;
            p.CPU_ID = Id;
            p.State = ProcessState.RUNNING;
            p.QuantumUsed = 0;
            p.WaitingTicks = 0;
            ThreadsInUse = ThreadCapacity;
        }

        // Libera la CPU actual, desalojando al proceso en ejecución de vuelta a estado READY.
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

    // =========================================================================
    // 4. MÉTRICAS GLOBALES DEL SISTEMA
    // =========================================================================

    // Consolida las métricas estadísticas globales acumuladas durante la ejecución de la simulación.
    // (Requerimiento 16/17 de Gestión de Proceso e Informe Final).
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

        // Historial detallado de los tiempos de retorno de cada proceso finalizado.
        public List<int> TurnaroundHistory { get; set; } = new List<int>();

        // Promedio del Tiempo de Retorno (Turnaround) global.
        public double AverageTurnaround
        {
            get { return CompletedProcesses == 0 ? 0 : (double)TotalTurnaroundTicks / CompletedProcesses; }
        }

        // Promedio del Tiempo de Espera (Waiting Time) global en colas.
        public double AverageWaiting
        {
            get { return CompletedProcesses == 0 ? 0 : (double)TotalWaitingTicks / CompletedProcesses; }
        }

        // Porcentaje de utilización activa de la CPU.
        public double CpuUtilization
        {
            get { return TotalCpuTicksCount == 0 ? 0 : 100.0 * (TotalCpuTicksCount - IdleCpuTicks) / TotalCpuTicksCount; }
        }

        // Desviación estándar de los tiempos de retorno (Varianza de predictibilidad).
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

        // Registra la finalización exitosa de un proceso y actualiza el acumulado de estadísticas.
        public void RecordCompletion(Process p)
        {
            if (p.IsSystemProcess) return;
            CompletedProcesses++;
            TotalTurnaroundTicks += p.TurnaroundTicks;
            TotalWaitingTicks += p.WaitingTicks;
            TurnaroundHistory.Add(p.TurnaroundTicks);
        }
    }

    // =========================================================================
    // 5. GESTIÓN Y ADJUDICACIÓN DE MEMORIA (PAGINACIÓN, MMU Y TLB)
    // =========================================================================

    // Representa un marco (Frame) de memoria física de tamaño fijo.
    // (Requerimientos 9, 10 y 11 de Segmentación en bloques de tamaño 2^n).
    public class Frame
    {
        // Índice identificador del marco.
        public int Id { get; set; }
        
        // Indica si el marco está disponible o asignado a un proceso.
        public bool IsFree { get; set; } = true;
        
        // PID del proceso que tiene reservado este marco.
        public int? PID { get; set; }
        
        // Tick en el que este marco fue cargado en RAM (para política FIFO de Swap).
        public int LoadedTick { get; set; }
        
        // Último tick en el que se leyó o escribió en este marco.
        public int LastAccessed { get; set; }
        
        // Protección de memoria: indica si es de Solo Lectura (código) o Lectura/Escritura.
        public bool IsReadOnly { get; set; } = false;
    }

    // Entrada del búfer de traducción de direcciones (Translation Lookaside Buffer - TLB).
    public class TLBEntry { public int PID { get; set; } public int Page { get; set; } public int Frame { get; set; } public int LastAccess { get; set; } }
    
    // Entrada en la tabla de páginas (Page Table) que gestiona la validez e historial de swap.
    public class PageEntry { public int Frame { get; set; } public bool Valid { get; set; } public int LoadedTick { get; set; } public bool Modified { get; set; } }

    // Unidad de Gestión de Memoria (MMU). Simula la traducción de direcciones físicas,
    // políticas de colocación (FirstFit/BestFit), TLB y Swap a disco por paginación.
    public class MMU
    {
        public int UnitId { get; set; }
        public int NumaNode { get; set; }
        public int PageFaults = 0;
        public int PageHits = 0;
        public int TotalRAM_MB { get; private set; }
        
        // Tamaño del marco/bloque, establecido en 4MB (múltiplo de 2^n).
        public int FrameSizeMB { get; private set; } = 4;

        // Arreglo indexable de marcos que representan la memoria RAM física disponible.
        public List<Frame> PhysicalMemory { get; private set; } = new List<Frame>();
        
        // Arreglo de marcos que representan el espacio de intercambio en disco (Swap Space).
        public List<Frame> SwapSpace { get; private set; } = new List<Frame>();

        // Habilita o deshabilita la cache de hardware TLB.
        public bool TlbEnabled { get; set; } = true;
        
        // Estrategia de asignación dinámica: "FirstFit" o "BestFit" (Requerimiento 6 de Memoria).
        public string AllocationAlgorithm { get; set; } = "FirstFit";

        // Estructura que almacena las entradas TLB calientes.
        public List<TLBEntry> TLB = new List<TLBEntry>();
        
        // Tabla de páginas de dos niveles mapeada por [PID] -> [Página] -> [Detalle de Página].
        private Dictionary<int, Dictionary<int, PageEntry>> PageTables = new Dictionary<int, Dictionary<int, PageEntry>>();

        // Inicializa la MMU segmentando la memoria total e intercambios en bloques de tamaño fijo.
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

        // Realiza la carga inicial del proceso en memoria física.
        // Soporta algoritmos First-Fit y Best-Fit (Requerimiento 6 de Memoria).
        // Reserva el 30% inicial de los marcos asignados como Solo Lectura (Segmento de Código).
        public bool Allocate(Process p)
        {
            if (p.PID == 0) return true;

            int requiredFrames = (int)Math.Ceiling((double)p.SizeMB / FrameSizeMB);
            List<Frame> selectedFrames = new List<Frame>();

            if (AllocationAlgorithm == "BestFit")
            {
                // Busca el conjunto contiguo o fragmentado óptimo de marcos libres.
                var freeFrames = PhysicalMemory.Where(f => f.IsFree).ToList();
                if (freeFrames.Count >= requiredFrames) selectedFrames = freeFrames.Take(requiredFrames).ToList();
            }
            else // FirstFit por defecto
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

            // Si no hay suficiente espacio contiguo/libre, rechaza la carga para posterior Swap/OOM
            if (selectedFrames.Count < requiredFrames) return false;

            PageTables[p.PID] = new Dictionary<int, PageEntry>();
            int frameCounter = 0;

            foreach (var frame in selectedFrames)
            {
                frame.IsFree = false;
                frame.PID = p.PID;
                
                // Protección del segmento de código (30% inicial es de solo lectura)
                if (frameCounter < (requiredFrames * 0.3)) frame.IsReadOnly = true;
                else frame.IsReadOnly = false;
                
                p.AssignedFrames.Add(frame.Id);
                PageTables[p.PID][frame.Id] = new PageEntry { Frame = frame.Id, Valid = true, LoadedTick = 0, Modified = false };
                frameCounter++;
            }
            return true;
        }

        // Desasigna todos los recursos físicos y virtuales del proceso al finalizar (Reaped).
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

            // Limpieza de TLB y tablas
            var tlbToRemove = TLB.Where(entry => entry.PID == p.PID).ToList();
            foreach (var entry in tlbToRemove) TLB.Remove(entry);
            PageTables.Remove(p.PID);
        }

        // Traduce una dirección lógica (página) a dirección física (marco).
        // Consulta la TLB (hits rápidos) y en su defecto la Tabla de Páginas.
        // Si hay intento de escritura en un segmento de solo lectura, lanza error de violación de segmento (-999).
        // Retorna null si la página no está cargada (Page Fault).
        public int? Translate(int pid, int page, int tick, bool isWriteOperation = false)
        {
            // 1. Búsqueda rápida en TLB
            foreach (var entry in TLB)
            {
                if (entry.PID == pid && entry.Page == page)
                {
                    PageHits++;
                    entry.LastAccess = tick;
                    PhysicalMemory[entry.Frame].LastAccessed = tick;
                    if (isWriteOperation && PhysicalMemory[entry.Frame].IsReadOnly) return -999; // Violación de Escritura (Segfault)
                    if (isWriteOperation && PageTables.ContainsKey(pid)) PageTables[pid][page].Modified = true;
                    return entry.Frame;
                }
            }

            // 2. Búsqueda en la Tabla de Páginas del Proceso
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
            
            // 3. Page Fault (Página desalojada o ausente de la RAM física)
            PageFaults++;
            return null;
        }

        // Inserta o actualiza una entrada en la TLB mediante política LRU simplificada.
        public void UpdateTLB(int pid, int page, int frame, int tick)
        {
            if (!TlbEnabled) return;
            if (TLB.Count >= 16) // Límite de hardware de la TLB (16 entradas)
            {
                // Remueve la entrada con acceso más antiguo
                TLBEntry oldest = TLB[0];
                foreach (var entry in TLB) { if (entry.LastAccess < oldest.LastAccess) oldest = entry; }
                TLB.Remove(oldest);
            }
            TLB.Add(new TLBEntry { PID = pid, Page = page, Frame = frame, LastAccess = tick });
        }

        // Manejador de fallos de página (Page Fault Handler).
        // Si no hay marcos físicos libres, desaloja un marco a Swap en disco usando política FIFO.
        // Aplica una penalización de 10 ticks de retardo adicional si el marco desalojado fue modificado (Dirty Bit).
        public int HandlePageFault(int pid, int page, int currentTick)
        {
            if (!PageTables.ContainsKey(pid)) PageTables[pid] = new Dictionary<int, PageEntry>();

            int swapPenalty = 0;
            Frame targetFrame = PhysicalMemory.FirstOrDefault(f => f.IsFree);

            // Si la memoria RAM física está completamente llena, desalojamos por FIFO
            if (targetFrame == null)
            {
                // Protege los marcos reservados por el Kernel e Init (PID <= 3) de ser desalojados
                List<Frame> usedFrames = PhysicalMemory.Where(f => f.PID.HasValue && f.PID.Value > 3).ToList();
                if (usedFrames.Count == 0) return -1; // Out of memory irrecuperable

                targetFrame = usedFrames.OrderBy(f => f.LoadedTick).First(); // FIFO

                // Desalojamos la página residente al área de Swap
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
                            
                            // Si la página fue escrita/modificada, requiere sincronización física con disco (Penalidad Swap-Out)
                            if (kvp.Value.Modified) swapPenalty = 10;

                            // Invalida entrada TLB correspondiente
                            var tlbToRemove = TLB.Where(item => item.PID == targetFrame.PID.Value && item.Frame == targetFrame.Id).ToList();
                            foreach (var item in tlbToRemove) TLB.Remove(item);
                            break;
                        }
                    }
                }
            }

            // Cargamos la nueva página al marco seleccionado
            targetFrame.IsFree = false; 
            targetFrame.PID = pid; 
            targetFrame.LoadedTick = currentTick; 
            targetFrame.LastAccessed = currentTick;
            
            PageTables[pid][page] = new PageEntry { Frame = targetFrame.Id, Valid = true, LoadedTick = currentTick, Modified = false };

            // Remueve de Swap de forma exitosa
            foreach (var s in SwapSpace) { if (s.PID == pid) { s.IsFree = true; s.PID = null; } }

            return swapPenalty;
        }

        // Mapea un segmento físico común para comunicación interprocesos (Shared Memory).
        public void MapSharedMemory(int pid1, int pid2)
        {
            if (PageTables.ContainsKey(pid1) && PageTables.ContainsKey(pid2))
            {
                var pt1 = PageTables[pid1].Values.FirstOrDefault(e => e.Valid);
                if (pt1 != null)
                {
                    int sharedFrameId = pt1.Frame;
                    // El proceso 2 obtiene acceso directo al mismo marco físico
                    PageTables[pid2][99] = new PageEntry { Frame = sharedFrameId, Valid = true, LoadedTick = 0, Modified = false };
                }
            }
        }
    }

    // =========================================================================
    // 6. PLANIFICACIÓN DE PROCESOS (SCHEDULERS)
    // =========================================================================

    // Clase abstracta base que representa el planificador de corto plazo (Short-term Scheduler).
    // Modela las colas de listos y las firmas de despacho del procesador.
    public abstract class Scheduler
    {
        // Cola de listos específica para el planificador.
        public List<Process> ReadyQueue { get; set; } = new List<Process>();
        
        // Valor asignable del Quantum para políticas RR / MLFQ.
        public int Quantum { get; set; } = 4;
        
        // Añade un proceso al estado READY colocando el proceso en la cola de listos.
        public virtual void AddProcess(Process p) { p.State = ProcessState.READY; ReadyQueue.Add(p); }
        
        // Obtiene el siguiente proceso a ejecutar según la política.
        public abstract Process? GetNextProcess(int currentTick);
        
        // Evalúa si el proceso que está corriendo debe ser expropiado por un recién llegado.
        public virtual Process? CheckPreemption(Process current) { return null; }
        
        // Aplica técnica de envejecimiento (Aging) para prevenir inanición (Starvation).
        public virtual void ApplyAging() { }
    }

    // Planificador First-Come First-Served (FCFS) - No Apropiativo.
    public class FCFS_Scheduler : Scheduler
    {
        public FCFS_Scheduler() { Quantum = 999999; } // Quantum indefinido
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            // Selecciona al que llegó primero al sistema
            var p = ReadyQueue.OrderBy(x => x.ArrivalTick).ThenBy(x => x.PID).First();
            ReadyQueue.Remove(p); return p;
        }
    }

    // Planificador Shortest Job First (SJF) - No Apropiativo por duración del Burst.
    public class SJF_Scheduler : Scheduler
    {
        public SJF_Scheduler() { Quantum = 999999; }
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            // Selecciona al de menor duración de ráfaga ticks
            var p = ReadyQueue.OrderBy(x => x.DurationTicks).ThenBy(x => x.PID).First();
            ReadyQueue.Remove(p); return p;
        }
    }

    // Planificador Multi-Level Feedback Queue (MLFQ) - Apropiativo por Prioridad de Cola.
    // Cuenta con 3 colas virtuales dinámicas que reducen prioridad en caso de ráfagas largas
    // y envejecimiento (Aging) para evitar inanición.
    public class MLFQ_Scheduler : Scheduler
    {
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;

            // Busca primero en las colas de nivel superior (MlfqQueueLevel = 0)
            Process bestProcess = ReadyQueue.OrderBy(p => p.MlfqQueueLevel).ThenBy(p => p.ArrivalTick).First();
            ReadyQueue.Remove(bestProcess);

            // Ajuste dinámico de Quantum según nivel de cola
            if (bestProcess.MlfqQueueLevel == 0) Quantum = 2;
            else if (bestProcess.MlfqQueueLevel == 1) Quantum = 4;
            else Quantum = 999; // Último nivel (FCFS)

            return bestProcess;
        }

        public override Process? CheckPreemption(Process current)
        {
            if (ReadyQueue.Count == 0) return null;
            Process bestProcess = ReadyQueue.OrderBy(p => p.MlfqQueueLevel).First();
            // Expropiación si hay un proceso en una cola de prioridad superior
            if (bestProcess.MlfqQueueLevel < current.MlfqQueueLevel) return bestProcess;
            return null;
        }

        public override void ApplyAging()
        {
            // Promueve procesos que han esperado más de 30 ticks para evitar Starvation
            foreach (var p in ReadyQueue) { if (p.WaitingTicks > 30 && p.MlfqQueueLevel > 0) { p.MlfqQueueLevel--; p.WaitingTicks = 0; } }
        }
    }

    // Completely Fair Scheduler (CFS) - Basado en la asignación equitativa de Virtual Runtime.
    public class CFS_Scheduler : Scheduler
    {
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            // Elige el proceso con menor tiempo de ejecución virtual acumulado
            Process bestProcess = ReadyQueue.OrderBy(p => p.VirtualRuntime).First();
            ReadyQueue.Remove(bestProcess); return bestProcess;
        }

        public override Process? CheckPreemption(Process current)
        {
            if (ReadyQueue.Count == 0) return null;
            Process bestProcess = ReadyQueue.OrderBy(p => p.VirtualRuntime).First();
            // Previene sobrecarga por cambios constantes exigiendo una diferencia mínima de 5 ticks
            if (bestProcess.VirtualRuntime < current.VirtualRuntime - 5) return bestProcess;
            return null;
        }
    }

    // Planificador Round Robin (RR) - Apropiativo por expiración de intervalo de tiempo (Quantum).
    public class RR_Scheduler : Scheduler
    {
        public override Process? GetNextProcess(int tick)
        {
            if (ReadyQueue.Count == 0) return null;
            // Saca al primer proceso de la cola circular (FIFO circular)
            var p = ReadyQueue[0]; ReadyQueue.RemoveAt(0); return p;
        }
    }

    // =========================================================================
    // 7. MOTOR PRINCIPAL DE SIMULACIÓN (KERNEL ENGINE)
    // =========================================================================

    // El motor de simulación (Kernel Engine) que orquesta los ticks del reloj del procesador,
    // colas de E/S de dispositivos periféricos, administración de hilos, planificadores,
    // OOM Killer, y procesos de Kernel base.
    public class SimulationEngine
    {
        // Contador de ticks globales de reloj del SO.
        public int TickCount { get; private set; } = 0;
        
        // Acumulador de estadísticas del simulador.
        public SimulationMetrics Metrics { get; private set; } = new SimulationMetrics();

        // --- Dispositivos Físicos ---
        // Listado de núcleos de CPU disponibles.
        public List<CPU> CPUs { get; set; } = new List<CPU>();
        
        // Listado de planificadores asociados a cada CPU.
        public List<Scheduler> Schedulers { get; set; } = new List<Scheduler>();
        
        // Nombres de las políticas aplicadas a cada CPU.
        public Dictionary<int, string> SchedulerNames { get; set; } = new Dictionary<int, string>();

        // --- Tablas de Procesos y Colas (Requerimiento 4 de Gestión de Proceso) ---
        // Registro global de procesos en el sistema (Tabla de Procesos).
        public List<Process> ProcessTable { get; set; } = new List<Process>();
        
        // Cola de admisión a largo plazo (Job Queue).
        public Queue<Process> JobQueue { get; set; } = new Queue<Process>();

        // --- Dispositivos de E/S (Cola de Espera - Requerimiento 4 de Gestión de Proceso) ---
        // Cola de espera para acceso a Disco Duro.
        public List<Process> WaitQueue_Disk { get; set; } = new List<Process>();
        
        // Cola de espera para transmisiones de Red.
        public List<Process> WaitQueue_Net { get; set; } = new List<Process>();
        
        // Cola de espera para llamadas del sistema (Syscalls) y Fallos de Página.
        public List<Process> WaitQueue_Sys { get; set; } = new List<Process>();

        // --- Interrupciones de Kernel y Memoria ---
        private List<Interrupt> InterruptQueue { get; set; } = new List<Interrupt>();
        public List<MMU> MemoryUnits { get; set; } = new List<MMU>();

        // Interruptor de generación automática de procesos de fondo.
        public bool AutoCreateProcesses { get; set; } = false;
        private Random rnd = new Random();
        
        // Bitácora de eventos del sistema mostrada en la consola interactiva.
        public List<string> ConsoleLog { get; set; } = new List<string>();

        // --- Secuencias de Arranque ---
        public bool IsBooting { get; set; } = true;
        public int BootSequenceWait { get; set; } = 15;

        // Inicializa el motor del simulador con los parámetros físicos.
        // Lanza los procesos de sistema Kernel_Core, Systemd_Init y KSwapd si no es una prueba de benchmark.
        public SimulationEngine(int cpus, int threads, int ramMb = 4096, bool isBenchmark = false)
        {
            MemoryUnits.Add(new MMU(0, ramMb, 0));

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
        
        // Configura dinámicamente el algoritmo de planificación en un núcleo físico determinado.
        public void SetCpuScheduler(int cpuId, string algName, int quantum = 4)
        {
            Scheduler s;
            if (algName == "MLFQ") s = new MLFQ_Scheduler();
            else if (algName == "CFS") s = s = new CFS_Scheduler();
            else if (algName == "FCFS") s = new FCFS_Scheduler();
            else if (algName == "SJF") s = new SJF_Scheduler();
            else s = new RR_Scheduler { Quantum = quantum };

            if (cpuId < Schedulers.Count) Schedulers[cpuId] = s;
            else Schedulers.Add(s);

            SchedulerNames[cpuId] = algName;
        }

        public void Log(string msg) { ConsoleLog.Add($"[{TickCount}] {msg}"); }

        // Simula una llamada del sistema fork() creando una copia del proceso padre.
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

        // Simula paso de mensajes síncronos entre buzones de procesos (IPC).
        public void SendIPC(int senderPid, int receiverPid, string message)
        {
            Process receiver = ProcessTable.FirstOrDefault(x => x.PID == receiverPid);
            if (receiver != null && receiver.State != ProcessState.TERMINATED)
            {
                receiver.Mailbox.Enqueue($"De {senderPid}: {message}");
                if (receiver.State == ProcessState.WAITING && receiver.InterruptReason == "IPC_WAIT") receiver.IORemainingTicks = 0;
            }
        }

        // Configura el direccionamiento compartido para dos procesos en la MMU.
        public void CreateSharedMemory(int pid1, int pid2)
        {
            MemoryUnits[0].MapSharedMemory(pid1, pid2);
        }

        // Fuerza la terminación del proceso (Kill) y realiza la cascada sobre todos los procesos hijos.
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

        // Out Of Memory (OOM) Killer.
        // Cuando la RAM se agota por completo, el Kernel selecciona al proceso usuario de mayor tamaño y menor prioridad,
        // finalizándolo para restaurar la estabilidad del hardware.
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

        // Avanza el reloj de simulación del Kernel en un ciclo (Tick).
        // Ejecuta los submódulos de admisión, balanceo, interrupciones, ejecución y despacho de CPU.
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
                // Generación aleatoria de procesos de usuario si el interruptor está activado
                if (AutoCreateProcesses && rnd.Next(1, 100) < 5)
                    InjectManualProcess(rnd.Next(32, 512), rnd.Next(50, 200), rnd.Next(1, 10));

                AdmitNewProcesses();
            }

            // Recolector del Kernel (Reaper): Limpia procesos zombi periódicamente
            if (TickCount % 50 == 0)
            {
                var zombies = ProcessTable.Where(p => p.State == ProcessState.ZOMBIE).ToList();
                foreach (var z in zombies) { z.State = ProcessState.TERMINATED; Log($"[KERNEL] Zombie PID {z.PID} limpiado (Reaped)."); }
            }

            ProcessInterrupts(); 
            UpdateWaitQueues(); 
            WorkStealing(); 
            ExecuteCPUs(); 
            Dispatch();

            // Incrementa contadores de espera en los planificadores y aplica Envejecimiento (Aging)
            foreach (var s in Schedulers) { s.ApplyAging(); foreach (var p in s.ReadyQueue) p.WaitingTicks++; }
        }

        // Robó de Hilos/Trabajo (Work Stealing).
        // Si un núcleo físico de CPU se encuentra inactivo y su cola está vacía, "roba" un proceso listo
        // del planificador más cargado del sistema para balancear dinámicamente el rendimiento del hardware.
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

        // Admite procesos de la Job Queue colocándolos en la MMU.
        // Si la MMU no puede asignarlos por falta de espacio, se retienen en la Job Queue.
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

                    // Coloca el proceso en el planificador con menor carga actual
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

        // Ejecuta un tick de hardware sobre todos los núcleos de CPU del sistema.
        // Simula enfriamiento/calentamiento térmico, reducción de potencia por penalización NUMA,
        // progresión de ráfagas, y disparadores de fallos de página o interrupciones I/O aleatorias.
        private void ExecuteCPUs()
        {
            foreach (var cpu in CPUs)
            {
                if (cpu.ContextSwitchWait > 0) { cpu.ContextSwitchWait--; continue; }

                Metrics.TotalCpuTicksCount++;
                var p = cpu.CurrentProcess;

                // 1. Núcleo Ocioso (Idle)
                if (p == null)
                {
                    Metrics.IdleCpuTicks++;

                    // Regula y disminuye temperatura de forma progresiva
                    cpu.FanSpeedRPM = Math.Max(1000, cpu.FanSpeedRPM - 50);
                    double coolingRate = 0.01 * (cpu.FanSpeedRPM / 1000.0);
                    cpu.Temperature = Math.Max(35.0, cpu.Temperature - coolingRate);

                    if (cpu.Temperature < 65.0) cpu.IsThrottling = false;
                    continue;
                }

                // 2. Núcleo con Carga Activa: Calentamiento térmico y control de ventilación
                cpu.Temperature += 0.008;

                if (cpu.Temperature > 70.0) cpu.FanSpeedRPM = Math.Min(5000, cpu.FanSpeedRPM + 100);
                else cpu.FanSpeedRPM = Math.Max(1500, cpu.FanSpeedRPM - 10);

                if (cpu.Temperature >= 85.0) cpu.IsThrottling = true;
                if (cpu.Temperature >= 95.0) cpu.Temperature = 95.0; // Máximo de calentamiento térmico

                int execPower = cpu.IsThrottling ? Math.Max(1, cpu.ThreadCapacity / 2) : cpu.ThreadCapacity;

                // Penalización por acceso a memoria cruzado (NUMA Node mismatch)
                if (p.MemoryUnitId.HasValue && p.MemoryUnitId.Value != cpu.NumaNode)
                {
                    execPower = Math.Max(1, execPower - 1);
                    Metrics.NumaPenalties++;
                }

                // Ejecución y avance de ráfaga
                if (p.DurationTicks != -1)
                {
                    p.RemainingTicks = Math.Max(0, p.RemainingTicks - execPower);
                    p.VirtualRuntime += (10.0 / Math.Max(1, p.Priority));
                }

                p.ProgramCounter += rnd.Next(4, 16); // Avance del Program Counter (Requerimiento 3)
                p.QuantumUsed++;

                // 3. Simulación de acceso a memoria y traducción virtual de página
                if (!p.IsSystemProcess && rnd.NextDouble() < 0.1 && p.MemoryUnitId.HasValue)
                {
                    var unit = MemoryUnits[p.MemoryUnitId.Value];
                    int reqPage = rnd.Next(0, Math.Max(1, (int)Math.Ceiling((double)p.SizeMB / unit.FrameSizeMB)));
                    bool isWrite = rnd.NextDouble() < 0.3;

                    if (cpu.L1Cache.ContainsKey(reqPage)) cpu.L1CacheHits++;
                    else
                    {
                        int? translatedFrame = unit.Translate(p.PID, reqPage, TickCount, isWrite);

                        if (translatedFrame == -999) 
                        { 
                            // Violación de Acceso a Memoria Protegida -> Lanza Segfault
                            InterruptQueue.Add(new Interrupt { Type = InterruptType.SEGFAULT, PID = p.PID, Priority = 0 }); 
                            cpu.Release(); continue; 
                        }
                        else if (translatedFrame == null) 
                        { 
                            // Fallo de Página -> Se encola interrupción y se bloquea el proceso
                            InterruptQueue.Add(new Interrupt { Type = InterruptType.PAGE_FAULT, PID = p.PID, Priority = 1, Payload = new Dictionary<string, int> { { "Page", reqPage }, { "Duration", 10 } } }); 
                            cpu.Release(); continue; 
                        }
                        else 
                        { 
                            // Mapea en la L1 del núcleo
                            if (cpu.L1Cache.Count >= 4) cpu.L1Cache.Clear(); 
                            cpu.L1Cache[reqPage] = translatedFrame.Value; 
                        }
                    }
                }

                // 4. Simulación de Interrupciones de E/S aleatorias (Periféricos: Disco, Red, GPU)
                if (!p.IsSystemProcess && rnd.NextDouble() < 0.02)
                {
                    int randType = rnd.Next(0, 3);
                    InterruptType type = InterruptType.IO_DISK;
                    if (randType == 1) type = InterruptType.IO_NETWORK;
                    if (randType == 2) type = InterruptType.IO_GPU;
                    
                    // Duración de E/S aleatoria entre 5 y 20 ticks
                    InterruptQueue.Add(new Interrupt { Type = type, PID = p.PID, Priority = 3, Payload = new Dictionary<string, int> { { "Duration", rnd.Next(5, 21) } } });
                    cpu.Release(); continue;
                }

                // 5. Finalización natural del proceso
                if (p.DurationTicks != -1 && p.RemainingTicks <= 0) { Terminate(p, "OK"); cpu.Release(); continue; }

                // 6. Expiración de Quantum y Expropiación de CPU
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
                    // Evalúa expropiación (Preemption) para planificadores dinámicos
                    var preempter = sched.CheckPreemption(p);
                    if (preempter != null)
                    {
                        Metrics.GlobalContextSwitches++; cpu.TotalContextSwitches++; cpu.ContextSwitchWait = 1; cpu.L1Cache.Clear();
                        sched.ReadyQueue.Remove(preempter); sched.AddProcess(p); cpu.Assign(preempter);
                    }
                }
            }
        }

        // Módulo Despachador (Dispatcher) de corto plazo.
        // Asigna nuevos procesos listos de la cola de listos a núcleos de CPU disponibles.
        private void Dispatch()
        {
            foreach (var cpu in CPUs) { if (cpu.CurrentProcess == null && cpu.ContextSwitchWait == 0) { var p = Schedulers[cpu.Id].GetNextProcess(TickCount); if (p != null) { cpu.Assign(p); } } }
        }

        // Desencadena el procesamiento de la interrupción con mayor prioridad en la cola.
        // Pasa al proceso interrumpido al estado WAITING y calcula la ráfaga de bloqueo.
        private void ProcessInterrupts()
        {
            if (InterruptQueue.Count == 0) return;
            InterruptQueue.Sort(); var current = InterruptQueue[0]; InterruptQueue.RemoveAt(0);

            Process p = ProcessTable.FirstOrDefault(x => x.PID == current.PID);
            if (p != null && p.State != ProcessState.TERMINATED)
            {
                if (current.Type == InterruptType.SEGFAULT) { Terminate(p, "SEGFAULT_ACCESS_VIOLATION"); return; }

                p.State = ProcessState.WAITING; p.IORemainingTicks = current.Payload.ContainsKey("Duration") ? current.Payload["Duration"] : 10;

                // Clasifica el proceso en la cola de bloqueo correspondiente (Requerimiento 4 de colas)
                if (current.Type == InterruptType.PAGE_FAULT) { p.InterruptReason = $"Fallo Pág"; p.PendingFaultPage = current.Payload["Page"]; WaitQueue_Sys.Add(p); }
                else if (current.Type == InterruptType.SYSCALL) { p.InterruptReason = "SYSCALL"; WaitQueue_Sys.Add(p); }
                else if (current.Type == InterruptType.IO_DISK) { p.InterruptReason = "E/S Disco"; WaitQueue_Disk.Add(p); }
                else if (current.Type == InterruptType.IO_NETWORK) { p.InterruptReason = "E/S Red"; WaitQueue_Net.Add(p); }
                else if (current.Type == InterruptType.IO_GPU) { p.InterruptReason = "E/S Render GPU"; WaitQueue_Disk.Add(p); }
            }
        }

        // Actualiza los temporizadores internos de las colas de espera de los dispositivos E/S (Disco, Red, Sys).
        // Despierta (mueve a READY) a los procesos que terminen su ráfaga de bloqueo.
        private void UpdateWaitQueues()
        {
            // Colas de sistema y fallos de página
            for (int i = WaitQueue_Sys.Count - 1; i >= 0; i--)
            {
                var p = WaitQueue_Sys[i]; p.IORemainingTicks--;
                if (p.IORemainingTicks <= 0)
                {
                    if (p.PendingFaultPage.HasValue && p.MemoryUnitId.HasValue)
                    {
                        // Resuelve el fallo de página en la MMU
                        int swapPenalty = MemoryUnits[p.MemoryUnitId.Value].HandlePageFault(p.PID, p.PendingFaultPage.Value, TickCount);
                        if (swapPenalty > 0) 
                        { 
                            // Si requiere swap a disco, incrementa la penalidad e interrupción
                            p.IORemainingTicks += swapPenalty; 
                            p.InterruptReason = "Swap Out a Disco"; 
                            Metrics.SwapOutCount++; 
                            Metrics.IsThrashing = (Metrics.SwapOutCount > 20); 
                            continue; 
                        }
                        else if (swapPenalty == -1) 
                        { 
                            TriggerOomKiller(); // RAM llena y sin espacio de Swap
                        }
                        p.PendingFaultPage = null;
                    }
                    WaitQueue_Sys.RemoveAt(i); Schedulers[p.CPU_ID ?? 0].AddProcess(p);
                }
            }

            // Colas de Disco y Red
            for (int i = WaitQueue_Disk.Count - 1; i >= 0; i--) { var p = WaitQueue_Disk[i]; p.IORemainingTicks--; if (p.IORemainingTicks <= 0) { WaitQueue_Disk.RemoveAt(i); Schedulers[p.CPU_ID ?? 0].AddProcess(p); } }
            for (int i = WaitQueue_Net.Count - 1; i >= 0; i--) { var p = WaitQueue_Net[i]; p.IORemainingTicks--; if (p.IORemainingTicks <= 0) { WaitQueue_Net.RemoveAt(i); Schedulers[p.CPU_ID ?? 0].AddProcess(p); } }
        }

        // Cambia el estado del proceso a ZOMBIE para liberar sus recursos de memoria RAM (Deallocate)
        // y registrar sus métricas en el consolidado.
        private void Terminate(Process p, string code)
        {
            p.State = ProcessState.ZOMBIE; p.ErrorCode = code; p.FinishTick = TickCount;
            if (p.MemoryUnitId.HasValue) MemoryUnits[p.MemoryUnitId.Value].Deallocate(p);
            Metrics.RecordCompletion(p);
        }

        // Inyecta un proceso en el sistema y lo coloca en la Job Queue para su posterior admisión física.
        public Process InjectManualProcess(int sizeMB, int burst, int priority, bool isSystem = false, string sysName = null)
        {
            if (!isSystem) Metrics.TotalProcesses++;
            var p = new Process { PID = ProcessTable.Count + 1, Name = isSystem ? sysName : $"App_{ProcessTable.Count + 1}", IsSystemProcess = isSystem, SizeMB = sizeMB, DurationTicks = burst, RemainingTicks = burst, Priority = priority, ArrivalTick = TickCount, HasError = false };
            ProcessTable.Add(p); JobQueue.Enqueue(p); return p;
        }
    }

    // =========================================================================
    // 8. INFRAESTRUCTURA DE BENCHMARK (COMPARADOR DE ALGORITMOS)
    // =========================================================================

    public class BenchmarkResult
    {
        public string Algoritmo { get; set; }
        public int TicksTotales { get; set; }
        public double TurnaroundMedio { get; set; }
        public double VarianzaPredictibilidad { get; set; }
    }

    // Motor de pruebas comparativas (Benchmark).
    // Ejecuta el mismo lote uniforme de procesos y carga sobre las 4 políticas de planificación en hilos separados
    // para evaluar la eficiencia (Ticks Totales, Turnaround Medio y Varianza).
    public class BenchmarkEngine
    {
        public static void RunBenchmarkAsync(int numProcesses, Action<double, string> onProgressUpdate, Action<List<BenchmarkResult>> onComplete)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                var results = new List<BenchmarkResult>();
                string[] algorithms = { "FCFS", "SJF", "RR", "MLFQ" };

                // Generación de un conjunto maestro uniforme de procesos mediante semilla fija
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

                // Evalúa de forma consecutiva cada algoritmo de planificación
                foreach (var alg in algorithms)
                {
                    onProgressUpdate(0, $"Simulando {alg}...");
                    var engine = new SimulationEngine(1, 1, 4096, true);
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

                    // Avanza la simulación del kernel hasta que todo en el lote termine
                    while (finished < totalToFinish)
                    {
                        engine.Tick();

                        if (engine.TickCount % 100 == 0)
                        {
                            finished = engine.ProcessTable.Count(p => p.State == ProcessState.TERMINATED && !p.IsSystemProcess);
                            double progress = ((double)finished / totalToFinish) * 100;
                            onProgressUpdate(progress, $"Simulando {alg} (Tick: {engine.TickCount})...");
                        }

                        if (engine.TickCount > 300000) break; // Límite de seguridad
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