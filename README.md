# 🖥️ Simulador So

Simulador So es una herramienta académica avanzada desarrollada en C# y WPF que simula, visualiza y evalúa el comportamiento interno de un Sistema Operativo moderno a nivel de Kernel.

Diseñado para demostrar conceptos complejos de arquitectura de computadoras, planificación de procesos y gestión de memoria de una manera visual, interactiva e inmersiva.

# ✨ Características Principales

## 🧠 1. Gestión de Memoria (MMU) Avanzada

### Paginación Visual en Vivo
Observa cómo se asignan los marcos de memoria (Frames) en la RAM física mediante bloques de colores neón únicos por cada proceso (PID).

### Virtual Memory & SWAP
Simulación real de Page Faults y desalojo a un archivo de paginación en disco (SWAP) cuando la RAM se llena.

### OOM Killer (Out-Of-Memory)
Si la RAM y el SWAP alcanzan el 100%, el Kernel activa rutinas para asesinar los procesos de menor prioridad y evitar el colapso del sistema.

### Memoria Compartida (IPC)
Capacidad para mapear direcciones virtuales de dos procesos distintos al mismo marco físico para comunicación entre procesos.

### Hardware TLB
Simulación y visualización de la caché Translation Lookaside Buffer.

## ⚙️ 2. Planificación de CPU Multi-Núcleo

Simulación de 4 CPUs simétricos trabajando en paralelo.

### Algoritmos Soportados

- FCFS (First-Come, First-Served)
- SJF (Shortest Job First)
- Round Robin (RR)
- MLFQ (Multilevel Feedback Queue)
- CFS (Completely Fair Scheduler - Estilo Linux)

### Work Stealing
Balanceo de carga automático. Si un CPU queda ocioso, robará procesos de las colas de listos de otros CPUs.

### Thermal Throttling & Active Cooling
Los procesadores simulan temperatura (hasta 95°C). Si se sobrecalientan por carga sostenida, su velocidad de ejecución se "estrangula" a la mitad, activando ventiladores dinámicos para enfriarse.

## 📊 3. Engine de Benchmark (Comparativa Científica)

Entorno aislado para evaluar el rendimiento de los planificadores.

Inyecta una carga de $N$ procesos idénticos (controlados por semilla) a través de los algoritmos FCFS, SJF, RR y MLFQ.

### Métricas Generadas

- Ticks Totales de Ejecución.
- Tiempo de Retorno Promedio (Turnaround).
- Varianza y Predictibilidad (Desviación Estándar).

### Veredicto Dinámico
Análisis automático que indica el algoritmo más rápido y el más estable tras cada prueba.

# 🚀 Cómo Empezar (Instalación)

## Prerrequisitos

- Visual Studio 2022 (o superior) con la carga de trabajo "Desarrollo de escritorio de .NET" instalada (para soporte de WPF).
- .NET Framework 4.7.2 o .NET 6.0/8.0 (Dependiendo de la configuración de tu proyecto).

## Pasos

### Clona el repositorio

```bash
git clone https://github.com/TuUsuario/SimuladorSo.git
```

### Abre la solución

Abre la solución `SimuladorSO.sln` en Visual Studio.

### Compila la solución

```text
Ctrl + Shift + B
```

### Ejecuta el proyecto

```text
F5
```

o el botón **Iniciar**.

# 🎮 Guía de Uso

La interfaz está dividida en 3 columnas principales y navegada mediante Pestañas (Tabs).

## Panel Izquierdo (Pestañas Dinámicas)

### CPUs y Procesos
Visualiza los procesadores trabajando, temperaturas, caché L1, la Ready Queue y la tabla general de procesos activos con sus barras de progreso.

### Gestión de Memoria
Monitorea la RAM física, el uso del SWAP y las métricas de Thrashing.

### I/O Queues
Visualiza los procesos bloqueados esperando operaciones de Disco, Red o Paginación.

### Benchmark
Ejecuta simulaciones masivas de $N$ procesos y obtén un análisis de eficiencia.

## Panel Derecho (Paleta Global Constante)

### Control de Tiempo
Inicia, pausa o altera la velocidad del reloj del simulador en vivo.

### Inyección Manual
Crea procesos con Memoria (MB), Duración (Ticks) y Prioridad específicas.

### Syscalls (Operaciones de Kernel)
Utiliza los botones rápidos para hacer Fork(), enviar señales SIGKILL, o mapear Memoria Compartida (Shared Mem) introduciendo los PIDs objetivo.

# 💡 Comandos Útiles

### Llenar RAM (Forzar SWAP)
Usa el botón "Llenar RAM (Forzar SWAP)" para inyectar repentinamente múltiples procesos pesados y ver en acción el Thrashing, el uso del disco y el OOM Killer.

### Auto-Spawn
Enciende el "Auto-Spawn" para simular un servidor recibiendo solicitudes (procesos) de forma aleatoria en segundo plano.

# 🛠️ Arquitectura y Tecnologías

## Backend/Kernel
C# Puro orientado a objetos. Diseñado sin bloqueos (non-blocking) utilizando tics de reloj emulados por DispatcherTimer.

## Frontend/UI
XAML (WPF). Interfaz oscura (Dark Theme) con alto contraste, utilizando Canvas dinámicos, DataGrids y gráficos de estado sólidos.

## Concurrencia
Integración de async/await y Task.Run para la ejecución del motor de Benchmark sin congelar el hilo de la interfaz de usuario.
