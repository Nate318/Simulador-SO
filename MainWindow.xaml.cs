using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SimuladorSO
{
    // Lógica de interacción para MainWindow.xaml.
    // Controla la interfaz gráfica y conecta los controles del usuario con el motor de simulación (SimulationEngine).
    public partial class MainWindow : Window
    {
        // Instancia principal del motor de simulación de sistema operativo (Kernel).
        private SimulationEngine engine;
        
        // Temporizador WPF para refrescar la interfaz gráfica y ejecutar ticks del reloj del kernel.
        private DispatcherTimer timer;

        // Paleta de colores asignada de forma dinámica por PID para representación visual en el lienzo.
        private Dictionary<int, SolidColorBrush> pidColors = new Dictionary<int, SolidColorBrush>();

        // Paleta básica de colores vívidos para diferenciar los procesos de usuario.
        private Color[] vividPalette = new Color[]
        {
            Color.FromRgb(255, 0, 255),
            Color.FromRgb(0, 255, 255),
            Color.FromRgb(0, 255, 0),
            Color.FromRgb(255, 255, 0),
            Color.FromRgb(255, 69, 0),
            Color.FromRgb(255, 20, 147),
            Color.FromRgb(0, 191, 255),
            Color.FromRgb(127, 255, 0)
        };

        // Constructor principal. Inicializa componentes gráficos, instancia el motor de simulación con 
        // 4 cores / 2 hilos por core, y arranca el temporizador de refresco en 250ms.
        public MainWindow()
        {
            InitializeComponent();
            engine = new SimulationEngine(cpus: 4, threads: 2);

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += Timer_Tick;

            PrintConsole("SO Arquitectura C# Pestañas Restaurada.");
            UpdateDashboard();
        }

        // Controla la velocidad de avance de la simulación del kernel de forma dinámica (duración del tick en ms).
        private void SldVelocidad_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (timer != null) timer.Interval = TimeSpan.FromMilliseconds(e.NewValue);
        }

        // Manejador de evento del temporizador (reloj principal). 
        // Avanza un Tick del Kernel y sincroniza las pestañas de Dashboard, Memoria y colas de E/S.
        // Captura errores graves del motor para evitar que la aplicación WPF se cierre inesperadamente.
        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                engine.Tick();
                UpdateDashboard();
                UpdateMemoryTab();
                UpdateIOQueues();

                // Si el motor reportó nuevas actividades, las concatena en la consola del sistema
                if (engine.ConsoleLog.Count > 0)
                {
                    TxtConsoleOutput.Text += engine.ConsoleLog.Last() + "\n";
                    // Limpieza automática de búfer para evitar sobrecarga de memoria
                    if (TxtConsoleOutput.Text.Length > 3000) TxtConsoleOutput.Text = TxtConsoleOutput.Text.Substring(1000);
                    ConsoleScroll.ScrollToBottom();
                }
            }
            catch (Exception ex)
            {
                timer.Stop();
                PrintConsole($"[ERROR GRAVE EVITADO] {ex.Message}");
            }
        }

        // Actualiza la pantalla principal (Dashboard): tarjetas de temperatura de CPUs,
        // monitores de los 5 estados de procesos en tiempo real y la grilla con información de PCBs activas.
        private void UpdateDashboard()
        {
            // Mapea el estado térmico y carga de cada núcleo físico
            var cpuDataList = engine.CPUs.Select(c => new {
                Title = $"CPU {c.Id} {(c.ContextSwitchWait > 0 ? "(CS)" : "")}",
                ProcessData = c.CurrentProcess != null ? $"PID: {c.CurrentProcess.PID} ({c.CurrentProcess.Name})\nRestante: {c.CurrentProcess.RemainingTicks} t" : "Ocioso",
                Thermals = $"Temp: {c.Temperature:F1}°C {(c.IsThrottling ? "[THROTTLING]" : "")}",
                TempColor = c.Temperature > 85 ? "#F44336" : (c.Temperature > 65 ? "#FF9800" : "#4CAF50"),
                BorderColor = c.IsThrottling ? "#F44336" : "#007ACC",
                FanSpeed = $"Fan: {c.FanSpeedRPM} RPM"
            }).ToList();
            CpuGrid.ItemsSource = cpuDataList;

            // Monitor de 5 Estados del Proceso (NEW, READY, RUNNING, WAITING, TERMINATED)
            ListStateNew.ItemsSource = engine.ProcessTable.Where(p => p.State == ProcessState.NEW).Select(p => $"PID {p.PID}").ToList();
            ListStateReady.ItemsSource = engine.ProcessTable.Where(p => p.State == ProcessState.READY).Select(p => $"PID {p.PID}").ToList();
            ListStateRunning.ItemsSource = engine.ProcessTable.Where(p => p.State == ProcessState.RUNNING).Select(p => $"PID {p.PID}").ToList();
            ListStateWaiting.ItemsSource = engine.ProcessTable.Where(p => p.State == ProcessState.WAITING).Select(p => $"PID {p.PID}").ToList();
            ListStateTerminated.ItemsSource = engine.ProcessTable.Where(p => p.State == ProcessState.TERMINATED || p.State == ProcessState.ZOMBIE).Select(p => $"PID {p.PID}").TakeLast(10).ToList();

            // Carga de PCBs de procesos activos en la grilla principal
            List<Process> activeProcs = new List<Process>();
            foreach (var p in engine.ProcessTable) if (p.State != ProcessState.TERMINATED) activeProcs.Add(p);
            GridProcesos.ItemsSource = activeProcs;
        }

        // Dibuja y refresca la pestaña de gestión de memoria:
        // - Texto informativo del uso de RAM física y OOM Kills.
        // - Estado de Thrashing por exceso de Swap.
        // - Renderizado de la tabla de la TLB.
        // - Dibujo de marcos en MemoryCanvas (RAM) y SwapCanvas (Intercambio en Disco).
        private void UpdateMemoryTab()
        {
            var mmu = engine.MemoryUnits[0];
            double swapUsagePercent = mmu.TotalRAM_MB == 0 ? 0 : ((double)mmu.UsedSwap_MB / mmu.TotalRAM_MB) * 100;

            LblMmuStats.Text = $"RAM Usada: {mmu.UsedRAM_MB} / {mmu.TotalRAM_MB} MB | OOM Kills: {engine.Metrics.OomKillsCount}";

            // Alerta visual de Thrashing si el Swap supera límites del 10%
            if (swapUsagePercent > 10)
            {
                LblThrashing.Text = "ESTADO: SWAP EN USO (THRASHING RIESGO)";
                LblThrashing.Foreground = Brushes.Orange;
            }
            else
            {
                LblThrashing.Text = "ESTADO: NORMAL";
                LblThrashing.Foreground = Brushes.SeaGreen;
            }

            // Actualiza grilla de la caché TLB
            GridTLB.ItemsSource = null;
            GridTLB.ItemsSource = mmu.TLB;

            // --- Renderizado Gráfico de la Memoria RAM Física ---
            MemoryCanvas.Children.Clear();
            if (mmu.PhysicalMemory.Count > 0)
            {
                double totalWidth = MemoryCanvas.ActualWidth == 0 ? 800 : MemoryCanvas.ActualWidth;
                double widthFrame = totalWidth / mmu.PhysicalMemory.Count;
                int startIdx = 0;

                // Agrupa bloques idénticos y consecutivos del mismo PID para dibujarlos como un único bloque
                for (int i = 1; i <= mmu.PhysicalMemory.Count; i++)
                {
                    bool isLast = i == mmu.PhysicalMemory.Count;
                    if (isLast || mmu.PhysicalMemory[i].PID != mmu.PhysicalMemory[i - 1].PID || mmu.PhysicalMemory[i].IsFree != mmu.PhysicalMemory[i - 1].IsFree)
                    {
                        var f = mmu.PhysicalMemory[i - 1];
                        int count = i - startIdx;
                        double w = count * widthFrame;
                        double x = startIdx * widthFrame;

                        Border block = new Border { Width = w, Height = 60, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), IsHitTestVisible = false };
                        TextBlock txt = new TextBlock { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10, FontWeight = FontWeights.Bold, ClipToBounds = true };

                        if (f.IsFree)
                        {
                            block.Background = Brushes.DimGray;
                            txt.Text = w >= 25 ? "Libre" : "";
                        }
                        else if (f.PID == 0)
                        {
                            block.Background = Brushes.Crimson; // Color reservado para el kernel
                            txt.Text = w >= 15 ? "OS" : "";
                        }
                        else
                        {
                            block.Background = GetColorForPID(f.PID.Value);
                            txt.Text = w >= 15 ? $"P{f.PID.Value}" : "";
                        }

                        if (!string.IsNullOrEmpty(txt.Text)) block.Child = txt;

                        Canvas.SetLeft(block, x);
                        MemoryCanvas.Children.Add(block);
                        startIdx = i;
                    }
                }
            }

            // --- Renderizado Gráfico del Espacio de Swap en Disco ---
            SwapCanvas.Children.Clear();
            if (mmu.SwapSpace.Count > 0)
            {
                double totalWidth = SwapCanvas.ActualWidth == 0 ? 800 : SwapCanvas.ActualWidth;
                double swpWidth = totalWidth / mmu.SwapSpace.Count;
                int startIdx = 0;

                for (int i = 1; i <= mmu.SwapSpace.Count; i++)
                {
                    bool isLast = i == mmu.SwapSpace.Count;
                    if (isLast || mmu.SwapSpace[i].PID != mmu.SwapSpace[i - 1].PID || mmu.SwapSpace[i].IsFree != mmu.SwapSpace[i - 1].IsFree)
                    {
                        var f = mmu.SwapSpace[i - 1];
                        int count = i - startIdx;
                        double w = count * swpWidth;
                        double x = startIdx * swpWidth;

                        Border block = new Border { Width = w, Height = 30, BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), IsHitTestVisible = false };
                        TextBlock txt = new TextBlock { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, FontSize = 10, FontWeight = FontWeights.Bold, ClipToBounds = true };

                        if (f.IsFree)
                        {
                            block.Background = Brushes.DimGray;
                            txt.Text = w >= 25 ? "Libre" : "";
                        }
                        else
                        {
                            block.Background = GetColorForPID(f.PID.Value);
                            txt.Text = w >= 15 ? $"P{f.PID.Value}" : "";
                        }

                        if (!string.IsNullOrEmpty(txt.Text)) block.Child = txt;

                        Canvas.SetLeft(block, x);
                        SwapCanvas.Children.Add(block);
                        startIdx = i;
                    }
                }
            }
        }

        // ================= SISTEMA DE HOVER DE MEMORIA =================
        
        // Traduce las coordenadas X del mouse al índice del marco de memoria RAM física correspondiente 
        // para mostrar información detallada del proceso residente en el hover.
        private void MemoryCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (engine == null || engine.MemoryUnits.Count == 0) return;
            var mmu = engine.MemoryUnits[0];
            if (mmu.PhysicalMemory.Count == 0 || MemoryCanvas.ActualWidth <= 0) return;

            double x = e.GetPosition(MemoryCanvas).X;
            double frameWidth = MemoryCanvas.ActualWidth / mmu.PhysicalMemory.Count;
            int frameIdx = (int)(x / frameWidth);

            if (frameIdx >= 0 && frameIdx < mmu.PhysicalMemory.Count)
            {
                ShowMemoryInfo(mmu.PhysicalMemory[frameIdx], false, mmu.FrameSizeMB);
            }
        }

        // Traduce las coordenadas X del mouse al índice del marco del Swap para mostrar detalles en el hover.
        private void SwapCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (engine == null || engine.MemoryUnits.Count == 0) return;
            var mmu = engine.MemoryUnits[0];
            if (mmu.SwapSpace.Count == 0 || SwapCanvas.ActualWidth <= 0) return;

            double x = e.GetPosition(SwapCanvas).X;
            double frameWidth = SwapCanvas.ActualWidth / mmu.SwapSpace.Count;
            int frameIdx = (int)(x / frameWidth);

            if (frameIdx >= 0 && frameIdx < mmu.SwapSpace.Count)
            {
                ShowMemoryInfo(mmu.SwapSpace[frameIdx], true, mmu.FrameSizeMB);
            }
        }

        // Restablece el texto de información detallada cuando el mouse abandona el área de memoria.
        private void ClearMemoryInfo(object sender, MouseEventArgs e)
        {
            LblHoverInfo.Text = "Pase el cursor sobre la RAM o SWAP a la izquierda para ver los detalles aquí...";
        }

        // Formatea y muestra los detalles del bloque de memoria seleccionado en base a su PCB residente.
        private void ShowMemoryInfo(Frame f, bool isSwap, int frameSizeMB)
        {
            if (f.IsFree)
            {
                LblHoverInfo.Text = $"{(isSwap ? "⬛ SWAP Libre" : "🟩 RAM Libre")}\nEste espacio de memoria de {frameSizeMB}MB está listo para ser asignado.";
                return;
            }
            if (f.PID == 0)
            {
                LblHoverInfo.Text = $"🟥 KERNEL OS (PID 0)\nBloque de memoria protegida por el sistema operativo.";
                return;
            }

            Process p = engine.ProcessTable.FirstOrDefault(px => px.PID == f.PID.Value);
            if (p != null)
            {
                LblHoverInfo.Text = $"{(isSwap ? "💽 [DESALOJADO A SWAP]" : "🖥️ [EN RAM]")} PID: {p.PID} - {p.Name}\n" +
                                    $"➤ Estado: {p.State} | Prio: {p.Priority}\n" +
                                    $"➤ Memoria Total de la App: {p.SizeMB} MB\n" +
                                    $"➤ Ejecución: {p.RemainingTicks} ticks restantes\n" +
                                    $"➤ Permiso de este marco: {(f.IsReadOnly ? "Solo Lectura" : "Lectura y Escritura")}";
            }
            else
            {
                LblHoverInfo.Text = $"PID {f.PID.Value} (Proceso Terminado/Zombi)\nMarco {frameSizeMB}MB pendiente de limpieza.";
            }
        }

        // Refresca las listas visuales correspondientes a las colas de espera de los dispositivos (Disco, Red, Sys/Page faults).
        // (Requerimiento 4 de colas de E/S).
        private void UpdateIOQueues()
        {
            ListDisk.Items.Clear(); foreach (var p in engine.WaitQueue_Disk) ListDisk.Items.Add($"[{p.IORemainingTicks}t] PID {p.PID} - Leyendo sectores HD");
            ListNet.Items.Clear(); foreach (var p in engine.WaitQueue_Net) ListNet.Items.Add($"[{p.IORemainingTicks}t] PID {p.PID} - Socket TCP Esperando");
            ListSys.Items.Clear(); foreach (var p in engine.WaitQueue_Sys) ListSys.Items.Add($"[{p.IORemainingTicks}t] PID {p.PID} - Page Fault/Syscall");
        }

        // Asigna y obtiene el color visual para representar un PID de proceso específico en los Canvas.
        private SolidColorBrush GetColorForPID(int pid)
        {
            if (pid == 1 || pid == 2) return Brushes.Maroon;
            if (!pidColors.ContainsKey(pid))
            {
                int index = (pid * 7) % vividPalette.Length;
                pidColors[pid] = new SolidColorBrush(vividPalette[index]);
            }
            return pidColors[pid];
        }

        // Activa (inicia) o desactiva (pausa) el ciclo de ticks del Kernel.
        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (timer.IsEnabled) { timer.Stop(); BtnToggle.Content = "▶ INICIAR KERNEL"; BtnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")); }
            else { timer.Start(); BtnToggle.Content = "⏸ PAUSAR KERNEL"; BtnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")); }
        }

        // Aplica una nueva configuración física (número de núcleos, hilos y RAM total), reiniciando el motor completo.
        private void BtnApplyConfig_Click(object sender, RoutedEventArgs e)
        {
            if (TxtConfigCpus != null && int.TryParse(TxtConfigCpus.Text, out int cpus) && int.TryParse(TxtConfigThreads.Text, out int threads) && int.TryParse(TxtConfigRam.Text, out int ram))
            {
                // Limita los valores de entrada a un rango seguro de simulación
                cpus = Math.Max(1, Math.Min(16, cpus));
                threads = Math.Max(1, Math.Min(8, threads));
                ram = Math.Max(512, Math.Min(32768, ram)) ;

                TxtConfigCpus.Text = cpus.ToString();
                TxtConfigThreads.Text = threads.ToString();
                TxtConfigRam.Text = ram.ToString();

                timer.Stop();
                engine = new SimulationEngine(cpus, threads, ram);
                TxtConsoleOutput.Text = "";
                PrintConsole($"[SISTEMA] Reinicio Hardware: {cpus} Cores, {threads} Hilos/Core, {ram}MB RAM total.");

                BtnToggle.Content = "⏸ PAUSAR KERNEL";
                BtnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                timer.Start();

                UpdateDashboard();
                UpdateMemoryTab();

                if (LblConfigStatus != null)
                {
                    LblConfigStatus.Text = "¡Hardware aplicado y sistema reiniciado con éxito!";
                    LblConfigStatus.Foreground = Brushes.SeaGreen;
                }
            }
        }

        // Inyecta un proceso individual (App) con tamaño, burst y prioridad definidos manualmente.
        // (Requerimiento 15 de Gestión de Proceso).
        private void BtnCrear_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtMem.Text, out int m) && int.TryParse(TxtTicks.Text, out int t) && int.TryParse(TxtPrio.Text, out int p))
            {
                engine.InjectManualProcess(m, t, p); PrintConsole($"[+] App inyectada."); UpdateDashboard();
            }
        }

        // Inyecta 30 procesos aleatorios de golpe para saturar la RAM física, provocando Swap masivo y el OOM Killer.
        private void BtnLlenarRam_Click(object sender, RoutedEventArgs e)
        {
            Random rnd = new Random();
            for (int i = 0; i < 30; i++)
            {
                int memAleatoria = rnd.Next(32, 512);
                int ticksAleatorios = rnd.Next(50, 800);
                int prioAleatoria = rnd.Next(1, 10);
                engine.InjectManualProcess(memAleatoria, ticksAleatorios, prioAleatoria);
            }
            PrintConsole($"[ALERTA] 30 Procesos aleatorios inyectados de golpe. RAM se saturará en instantes.");
        }

        // Enciende o apaga la generación automática/aleatoria de procesos de fondo.
        private void BtnAuto_Click(object sender, RoutedEventArgs e)
        {
            engine.AutoCreateProcesses = !engine.AutoCreateProcesses;
            if (engine.AutoCreateProcesses) { BtnAuto.Content = "Auto-Spawn: ON"; BtnAuto.Background = Brushes.SeaGreen; }
            else { BtnAuto.Content = "Auto-Spawn: OFF"; BtnAuto.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#607D8B")); }
        }

        // Envía una señal SIGKILL para terminar de forma forzada un proceso específico.
        private void BtnKill_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtActionPID.Text, out int pid)) { engine.KillProcess(pid); PrintConsole($"[COMANDO] Kill PID {pid}."); UpdateDashboard(); }
        }

        // Fuerza al proceso indicado a duplicar su hilo mediante una llamada fork().
        private void BtnFork_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtActionPID.Text, out int pid)) { engine.ForkProcess(pid); PrintConsole($"[COMANDO] Fork PID {pid}."); UpdateDashboard(); }
        }

        // Configura una región de memoria compartida entre dos procesos indicados (Shared Memory).
        private void BtnIpc_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtIpcFrom.Text, out int from) && int.TryParse(TxtIpcTo.Text, out int to))
            {
                engine.CreateSharedMemory(from, to);
                UpdateDashboard();
            }
        }

        // Actualiza dinámicamente el valor del Quantum en el planificador Round Robin en tiempo real.
        // (Requerimiento 7 de Gestión de Proceso).
        private void BtnApplyQuantum_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtQuantum.Text, out int q) && q > 0)
            {
                foreach (var sched in engine.Schedulers)
                {
                    if (sched is RR_Scheduler rr) rr.Quantum = q;
                }
                PrintConsole($"> Quantum de Round Robin actualizado a {q} ticks.");
            }
        }

        // Inicia una prueba de carga en segundo plano (BenchmarkEngine) de N procesos de carga uniforme.
        // Compara en tiempo real los algoritmos (FCFS, SJF, RR, MLFQ) y muestra los resultados y un veredicto del Kernel.
        // (Requerimiento 17 de políticas de planificación comparadas).
        private async void BtnRunBenchmark_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtNumN.Text, out int n))
            {
                BtnRunBenchmark.IsEnabled = false;
                ListBenchmarkLive.Items.Clear();

                LblFastest.Text = "⚡ Más Rápido (Ticks Totales): Calculando...";
                LblSlowest.Text = "🐢 Más Lento (Ticks Totales): Calculando...";
                LblPredictable.Text = "🎯 Más Predecible (Varianza Baja): Calculando...";
                LblBestTurnaround.Text = "⏱️ Mejor Turnaround Promedio: Calculando...";
                LblVeredicto.Text = "Veredicto: Ejecutando simulaciones...";

                PrintConsole($"[BENCHMARK] Iniciando simulación en 2do plano para N={n} procesos...");

                // Invoca la simulación en segundo plano para no congelar el hilo principal de la UI de WPF
                BenchmarkEngine.RunBenchmarkAsync(n,
                    (progreso, mensaje) =>
                    {
                        Dispatcher.Invoke(() => {
                            BenchmarkProgress.Value = progreso;
                            LblBenchmarkStatus.Text = mensaje;
                            if (!ListBenchmarkLive.Items.Contains(mensaje))
                            {
                                ListBenchmarkLive.Items.Add(mensaje);
                                ListBenchmarkLive.ScrollIntoView(mensaje);
                            }
                        });
                    },
                    (resultados) =>
                    {
                        Dispatcher.Invoke(() => {
                            GridBenchmark.ItemsSource = null;
                            GridBenchmark.ItemsSource = resultados;
                            LblBenchmarkStatus.Text = "Prueba de Carga Finalizada.";
                            ListBenchmarkLive.Items.Add(">>> TODAS LAS SIMULACIONES COMPLETADAS <<<");
                            ListBenchmarkLive.ScrollIntoView(ListBenchmarkLive.Items[ListBenchmarkLive.Items.Count - 1]);
                            BtnRunBenchmark.IsEnabled = true;

                            if (resultados.Count > 0)
                            {
                                // Extrae los mejores y peores algoritmos según métricas del benchmark
                                var fastest = resultados.OrderBy(r => r.TicksTotales).First();
                                var slowest = resultados.OrderByDescending(r => r.TicksTotales).First();
                                var predictable = resultados.OrderBy(r => r.VarianzaPredictibilidad).First();
                                var bestTurnaround = resultados.OrderBy(r => r.TurnaroundMedio).First();

                                LblFastest.Text = $"⚡ Más Rápido (Ticks Totales): {fastest.Algoritmo} ({fastest.TicksTotales}t)";
                                LblSlowest.Text = $"🐢 Más Lento (Ticks Totales): {slowest.Algoritmo} ({slowest.TicksTotales}t)";
                                LblPredictable.Text = $"🎯 Más Predecible (Varianza): {predictable.Algoritmo} ({predictable.VarianzaPredictibilidad})";
                                LblBestTurnaround.Text = $"⏱️ Mejor Turnaround Prom: {bestTurnaround.Algoritmo} ({bestTurnaround.TurnaroundMedio}t)";

                                LblVeredicto.Text = $"Veredicto del Kernel: Para una carga bruta de {n} procesos, el algoritmo '{fastest.Algoritmo}' procesó todo el lote más rápido. Sin embargo, para entornos interactivos, '{predictable.Algoritmo}' ofrece la mayor estabilidad y ausencia de picos en los tiempos de respuesta (menor varianza).";
                            }

                            PrintConsole($"[BENCHMARK] Finalizado. Revisa la tabla de resultados y el resumen de eficiencia.");
                        });
                    }
                );
            }
        }

        // Cambia dinámicamente el algoritmo de planificación global asociado a todos los núcleos de CPU activos.
        private void CmbPolitica_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (engine == null || CmbPolitica.SelectedItem == null) return;
            string txt = (CmbPolitica.SelectedItem as ComboBoxItem).Content.ToString();
            string p = "RR";
            if (txt.Contains("MLFQ")) p = "MLFQ";
            else if (txt.Contains("CFS")) p = "CFS";
            else if (txt.Contains("FCFS")) p = "FCFS";
            else if (txt.Contains("SJF")) p = "SJF";

            int currentQuantum = 4;
            if (TxtQuantum != null && int.TryParse(TxtQuantum.Text, out int q) && q > 0) currentQuantum = q;

            foreach (var c in engine.CPUs) engine.SetCpuScheduler(c.Id, p, currentQuantum);
            PrintConsole($"> Schedulers globales = {p}.");
        }

        // Auxiliar para escribir logs con scroll automático a la consola interactiva.
        private void PrintConsole(string msg) { TxtConsoleOutput.Text += msg + "\n"; ConsoleScroll.ScrollToBottom(); }
    }
}