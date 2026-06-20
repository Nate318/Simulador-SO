using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SimuladorSO
{
    public partial class MainWindow : Window
    {
        private SimulationEngine engine;
        private DispatcherTimer timer;

        private Dictionary<int, SolidColorBrush> pidColors = new Dictionary<int, SolidColorBrush>();

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

        public MainWindow()
        {
            InitializeComponent();
            engine = new SimulationEngine(cpus: 4, threads: 2);

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            timer.Tick += Timer_Tick;

            PrintConsole("SO Arquitectura Pestañas (Termales Corregidos).");
            UpdateDashboard();
        }

        private void SldVelocidad_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (timer != null) timer.Interval = TimeSpan.FromMilliseconds(e.NewValue);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            try
            {
                engine.Tick();
                UpdateDashboard();
                UpdateMemoryTab();
                UpdateIOQueues();

                if (engine.ConsoleLog.Count > 0)
                {
                    TxtConsoleOutput.Text += engine.ConsoleLog.Last() + "\n";
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

        private void UpdateDashboard()
        {
            var cpuDataList = engine.CPUs.Select(c => new {
                Title = $"CPU {c.Id} {(c.ContextSwitchWait > 0 ? "(CS)" : "")}",
                ProcessData = c.CurrentProcess != null ? $"PID: {c.CurrentProcess.PID} ({c.CurrentProcess.Name})\nRestante: {c.CurrentProcess.RemainingTicks} t" : "Ocioso",
                Thermals = $"Temp: {c.Temperature:F1}°C {(c.IsThrottling ? "[THROTTLING]" : "")}",
                TempColor = c.Temperature > 85 ? "#F44336" : (c.Temperature > 65 ? "#FF9800" : "#4CAF50"),
                BorderColor = c.IsThrottling ? "#F44336" : "#007ACC",
                FanSpeed = $"Fan: {c.FanSpeedRPM} RPM"
            }).ToList();
            CpuGrid.ItemsSource = cpuDataList;

            List<string> readyPids = new List<string>();
            foreach (var sched in engine.Schedulers)
            {
                foreach (var p in sched.ReadyQueue) readyPids.Add($"PID {p.PID}");
            }
            VisualQueueGrid.ItemsSource = readyPids;

            List<Process> activeProcs = new List<Process>();
            foreach (var p in engine.ProcessTable) if (p.State != ProcessState.TERMINATED) activeProcs.Add(p);
            GridProcesos.ItemsSource = activeProcs;
        }

        private void UpdateMemoryTab()
        {
            var mmu = engine.MemoryUnits[0];
            double swapUsagePercent = mmu.TotalRAM_MB == 0 ? 0 : ((double)mmu.UsedSwap_MB / mmu.TotalRAM_MB) * 100;

            LblMmuStats.Text = $"RAM Usada: {mmu.UsedRAM_MB} / {mmu.TotalRAM_MB} MB | OOM Kills: {engine.Metrics.OomKillsCount}";

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

            GridTLB.ItemsSource = null;
            GridTLB.ItemsSource = mmu.TLB;

            MemoryCanvas.Children.Clear();
            if (mmu.PhysicalMemory.Count > 0)
            {
                double widthFrame = MemoryCanvas.ActualWidth / mmu.PhysicalMemory.Count;
                double currentX = 0;
                foreach (var f in mmu.PhysicalMemory)
                {
                    Rectangle r = new Rectangle { Width = widthFrame, Height = MemoryCanvas.ActualHeight, Stroke = Brushes.Black, StrokeThickness = 0.3 };
                    if (f.IsFree) r.Fill = Brushes.DimGray;
                    else if (f.PID == 0) r.Fill = Brushes.Crimson;
                    else r.Fill = GetColorForPID(f.PID.Value);
                    Canvas.SetLeft(r, currentX); MemoryCanvas.Children.Add(r); currentX += widthFrame;
                }
            }

            SwapCanvas.Children.Clear();
            if (mmu.SwapSpace.Count > 0)
            {
                double swpWidth = SwapCanvas.ActualWidth / mmu.SwapSpace.Count;
                double sX = 0;
                foreach (var f in mmu.SwapSpace)
                {
                    Rectangle r = new Rectangle { Width = swpWidth, Height = SwapCanvas.ActualHeight, Stroke = Brushes.Black, StrokeThickness = 0.3 };
                    if (f.IsFree) r.Fill = Brushes.DimGray;
                    else r.Fill = GetColorForPID(f.PID.Value);
                    Canvas.SetLeft(r, sX); SwapCanvas.Children.Add(r); sX += swpWidth;
                }
            }
        }

        private void UpdateIOQueues()
        {
            ListDisk.Items.Clear(); foreach (var p in engine.WaitQueue_Disk) ListDisk.Items.Add($"[{p.IORemainingTicks}t] PID {p.PID} - Leyendo sectores HD");
            ListNet.Items.Clear(); foreach (var p in engine.WaitQueue_Net) ListNet.Items.Add($"[{p.IORemainingTicks}t] PID {p.PID} - Socket TCP Esperando");
            ListSys.Items.Clear(); foreach (var p in engine.WaitQueue_Sys) ListSys.Items.Add($"[{p.IORemainingTicks}t] PID {p.PID} - Page Fault/Syscall");
        }

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

        private void BtnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (timer.IsEnabled) { timer.Stop(); BtnToggle.Content = "▶ INICIAR KERNEL"; BtnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#007ACC")); }
            else { timer.Start(); BtnToggle.Content = "⏸ PAUSAR KERNEL"; BtnToggle.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")); }
        }

        private void BtnCrear_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtMem.Text, out int m) && int.TryParse(TxtTicks.Text, out int t) && int.TryParse(TxtPrio.Text, out int p))
            {
                engine.InjectManualProcess(m, t, p); PrintConsole($"[+] App inyectada."); UpdateDashboard();
            }
        }

        private void BtnLlenarRam_Click(object sender, RoutedEventArgs e)
        {
            Random rnd = new Random();
            for (int i = 0; i < 30; i++) engine.InjectManualProcess(128, 500, 5);
            PrintConsole($"[ALERTA] Fuga inyectada. RAM se saturará, se usará SWAP y el OOM Killer actuará.");
        }

        private void BtnAuto_Click(object sender, RoutedEventArgs e)
        {
            engine.AutoCreateProcesses = !engine.AutoCreateProcesses;
            if (engine.AutoCreateProcesses) { BtnAuto.Content = "Auto-Spawn: ON"; BtnAuto.Background = Brushes.SeaGreen; }
            else { BtnAuto.Content = "Auto-Spawn: OFF"; BtnAuto.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#607D8B")); }
        }

        private void BtnKill_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtActionPID.Text, out int pid)) { engine.KillProcess(pid); PrintConsole($"[COMANDO] Kill PID {pid}."); UpdateDashboard(); }
        }

        private void BtnFork_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtActionPID.Text, out int pid)) { engine.ForkProcess(pid); PrintConsole($"[COMANDO] Fork PID {pid}."); UpdateDashboard(); }
        }

        private void BtnIpc_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtIpcFrom.Text, out int from) && int.TryParse(TxtIpcTo.Text, out int to))
            {
                engine.CreateSharedMemory(from, to);
                UpdateDashboard();
            }
        }

        // ================= BENCHMARK CON RESUMEN DINÁMICO AÑADIDO =================
        private async void BtnRunBenchmark_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtNumN.Text, out int n))
            {
                BtnRunBenchmark.IsEnabled = false;
                ListBenchmarkLive.Items.Clear();

                // Reiniciar visuales de resumen
                LblFastest.Text = "⚡ Más Rápido (Ticks Totales): Calculando...";
                LblSlowest.Text = "🐢 Más Lento (Ticks Totales): Calculando...";
                LblPredictable.Text = "🎯 Más Predecible (Varianza Baja): Calculando...";
                LblBestTurnaround.Text = "⏱️ Mejor Turnaround Promedio: Calculando...";
                LblVeredicto.Text = "Veredicto: Ejecutando simulaciones...";

                PrintConsole($"[BENCHMARK] Iniciando simulación en 2do plano para N={n} procesos...");

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

                            // LOGICA NUEVA AÑADIDA: Cálculo del Resumen
                            if (resultados.Count > 0)
                            {
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

        private void CmbPolitica_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (engine == null || CmbPolitica.SelectedItem == null) return;
            string txt = (CmbPolitica.SelectedItem as ComboBoxItem).Content.ToString();
            string p = "RR";
            if (txt.Contains("MLFQ")) p = "MLFQ";
            else if (txt.Contains("CFS")) p = "CFS";
            else if (txt.Contains("FCFS")) p = "FCFS";
            else if (txt.Contains("SJF")) p = "SJF";

            foreach (var c in engine.CPUs) engine.SetCpuScheduler(c.Id, p, 4);
            PrintConsole($"> Schedulers globales = {p}.");
        }

        private void PrintConsole(string msg) { TxtConsoleOutput.Text += msg + "\n"; ConsoleScroll.ScrollToBottom(); }
    }
}