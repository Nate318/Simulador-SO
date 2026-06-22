# REQUERIMIENTOS PARA TRABAJO
## Gestión de Procesos y Memoria

### Ingreso de un proceso
* Memoria a usar por un proceso.
    * Tamaño de programa ejecutable.
    * Tamaño del segmento de datos (variables, registros, otros) requeridos. (% del ejecutable).
    * Memoria variable a asignar durante la ejecución del proceso. (% del ejecutable)
* La memoria debe ser previamente segmentada en bloques, en donde el tamaño de un bloque debe ser múltiplo de 2ⁿ - Ejm: 32KB a 2 MB.
* El proceso previamente debe ser segmentada en bloques, en donde el tamaño de un bloque debe ser múltiplo de 2ⁿ - Ejm: 32KB a 2 MB.
* El tamaño del bloque de la memoria debe ser igual al tamaño del bloque del proceso.
* Desperdicios de memoria: bloques que no han sido ocupados totalmente.
* Verificar que se tiene el total de memoria requerida:
    * Requerido por el proceso.
    * Controlar los bloques de memoria (libres y ocupados), hacerlo constantemente.
    * Controlar que el desperdicio sea mínimo, hacerlo constantemente.
    * Para todos los casos debe durar todo el tiempo que la simulación se encuentra activo. 

---

### Gestión de Proceso

1. Administración de los 5 estados en los que un proceso se puede encontrar. Se debe considerar las actividades para trasladarse de estado a estado.
2. Administración de las interrupciones, en esta se deben considerar los distintos tipos de este (ejm. Requerimiento de E/S, finalización de un requerimiento de E/S, de “Timer”, etc.) la cantidad y tipos de interrupciones deben ser indicados en el supuesto del proyecto.
3. Administración de la PCB, definiendo en memoria los campos en donde se almacenarán cada campo requerido por la PCB, en estas actividades no se deben obviar el uso del PROGRAM COUNTER. La cantidad de campos definidos en su programa se debe indicar en los supuestos del proyecto.
4. Administración de las 3 colas de procesos: totales, listos, E/S (tener como mínimo 5 dispositivos)
5. Funciones del módulo planificador (SCHEDULER), a largo, a mediano y a largo plazo. Se deben considerar (para la planificación a corto plazo las políticas: FCFS, SJF, ASALTO-ROBIN y por Prioridades.
6. Los esquemas de planificación a programarse deben sujetarse a lo definido en clases.
    * **a.** Cuando un proceso se bloquea: por ejemplo, cuando inicia una operación de E/S o espera a que termine un hijo, etc.
    * **b.** Cuando un proceso cambia del estado ejecutando al estado listo. Por ejemplo, al ocurrir una interrupción.
    * **c.** Cuando ocurre una interrupción de E/S y un proceso pasa del estado bloqueado a Listo.
    * **d.** Cuando se crea un proceso.
    * **e.** Cuando un proceso finaliza su ejecución.
    * Cuando ocurre a o e, el planificador es invocado debido a que el proceso en ejecución libera el procesador.
    * Si el planificador es invocado cuando ocurre b, c o d, se dice que este es “expropiativo/apropiativo”, ya que puede quitar el procesador al proceso que estaba en ejecución.
7. Las políticas de ordenamiento de colas estarán definidas en los contextos: apropiativo y no apropiativo. El valor del “quantum” para la política RR será ingresado como parámetro en inicio de la simulación y podrá ser cambiado ingresándolo nuevamente.
8. Es opcional si el sistema operativo hace los cambios según los procesos ingresados.
9. Funciones del módulo despachador (DISPATCHER), principalmente las actividades para el cambio de contexto.
10. Administración de errores, se debe considerar construir un módulo que se encargue de verificar la actividad correcta del proceso. Si no es correcta la actividad del proceso cancelara con un código de error que se guardara en las estadísticas y PCB del proceso.
11. La cantidad de procesos errados será aleatoria y será el 0.5% (cinco por cada mil procesos) de la cantidad de procesos acumulados al simulador.
12. La cantidad de interrupciones que se producen en un proceso serán aleatorios y la cantidad de estos estará entre 5 y 20, dependiente del tamaño de tamaño del proceso y burst-time.
13. Los tiempos de duración de las interrupciones deben ser aleatorias con un rango de duración entre 5 y 20 unidades de tiempo dependiendo del burt-time.
14. La ecuación matemática del calculo de los valores aleatorios deben estar en el informe del proyecto y mostrados/sustentados en su presentación.
15. Se podrán generar aleatoriamente los procesos que se ejecutarán (indicando la cantidad de procesos) o se ingresarán manualmente el proceso con los parámetros de tamaño del proceso y el tiempo estimado de uso de CPU (burst-time).
16. El sistema de emulación deberá mostrar en línea la activad de cada proceso, sus cambios de estado, los procesos propios del S.O. y los procesos usuarios, los datos de la PCB, del PC, y otros que considere conveniente el equipo (cuanto mas relevante el dato mostrado mejor presentación y por consiguiente mejor nota).
17. En el simulador construido se deberán ingresar y “ejecutar” 20 procesos que serán ingresadas en el sistema de manera secuencial y manual cada grupo definirá el tiempo de demora de cada uno de ellos, evaluar para cada uno de ellos el uso de CPU, tiempos de espera y tiempo de respuesta. Por cada una de las políticas definidas en el punto 5 y en los dos contextos.

---

### Gestión de memoria

1. Considerando que un proceso debe ser cargado en su totalidad en memoria para iniciar el proceso.
2. Se debe considerar los dos métodos de asignación dinámica de la memoria. (stack, heap).
3. La asignación de direcciones es a inicio de proceso y considerará direccionamiento físico. La dirección inicial del proceso debe figurar en la PBC del proceso.
4. En este caso no usa MMU por ser direccionamiento físico del procesos.
5. La administración de la memoria debe usar el método “Mapa de Bits” o “Lista encadenada” (a criterio del grupo de trabajo).
6. Se deben aplicar los tres tipos de estrategia de asignación.
7. Los 20 procesos usados para evaluar las políticas de procesos del trabajo anterior. Aplicar las tres estrategias de asignación y concluir cual de estos es el mas eficiente en “performace” (rendimiento del proceso) y en optimización del fraccionamiento de la memoria.

---

### Gestión de E/S

1. En el mismo sistema desarrollado hasta el momento deberán simular la captura de ñlos siguientes dispositivos:
    1. Teclado.
    2. Disco.
    3. Impresora.
2. Generación de la interrupción haciendo un requerimiento de E/S por los procesos de la simulación. Esto administrado según código de interrupción.
3. Administración del estado del proceso, según el requerimiento de E/S.
4. Parámetros enviados a controlador para el requerimiento de E/S.
5. Deberán considerar, administración de las colas de atención de los requerimientos.
6. Generación de la interrupción por la terminación del requerimiento.
7. Carga/descarga de región de memoria
8. Administración del estado del proceso.
9. Para el dispositivo Teclado, capturar la señal que llega y actuar (Cancelación o Continuación).
10. Por el total de dispositivos deberán haber como mínimo 10 interrupciones en el proceso ejecutándose. Siendo aleatorio su asignación.
11. Los tiempos de duración de cada interrupción deberán ser aleatorias según características del proceso (peso de proceso y tiempo requerido por el proceso.

---

### Consideraciones Generales y finales

1. Se debe presentar un informe final de acuerdo a formato indicado. Este debe contener las conclusiones relacionados a las políticas de planificación de procesos y políticas de asignación de memoria.
2. Se debe presentar una presentación (en PPT u otro que consideren) en donde se indique y explique cuales son los objetivos (generales y específicos) del trabajo realizado, que problemática resuelve el trabajo, las actividades realizadas en el trabajo, las conclusiones del trabajo, los logros obtenidos y las próximas actividades que se pueden considerar.
3. La duración de la presentación final del trabajo no debe extenderse mas allá de 30 minutos, siendo 15 minutos para explicación de PPT (deben intervenir todos los participantes del grupo), 10 min. para la presentación del software y 5 min. para preguntas.
4. En el informe a modo de anexo se debe considerar la documentación del sistema a presentar, el FRAMEWORK usado, los manuales de instalación, manuales de usuario y las funciones de cada componente implementado.
5. El informe debe ser subido a la carpeta del UNIVIRTUAL
6. El 40% (8 puntos) de la calificación será la presentación y verificación del software, en el que se considerará la ejecución de la compilación y generación del ejecutable así como la verificación del programa fuente y FRAMEWORK, cargados en el carpeta virtual. En caso de falla solo recibirá 1 punto en esta sección.
7. El 30% (6 puntos) de la calificación será para el informe final, debe contener todo lo indicado en líneas arriba. Estos puntos serán basados en la revisión de lo cargado en la carpeta virtual.
8. El 30% (6 puntos) de la calificación será producto de la presentación, exposición y respuesta a preguntas sobre el trabajo, Esta nota será por cada estudiante que sustente el trabajo.

---

### Descripción del Diagrama: Ingreso de un Proceso

Este diagrama de bloques detalla el flujo lógico, los módulos funcionales y las colas que intervienen desde la creación de un proceso hasta su ejecución en la CPU y la gestión de sus eventos.

#### 1. Origen y Creación de Procesos
* **Fuentes de Entrada:** Los procesos pueden originarse de dos maneras:
    * **Proceso Creado por el SO:** Procesos internos del sistema operativo.
    * **Proceso Ingresado por USUARIO:** Procesos lanzados de forma externa por el usuario.
* **Módulo de Administración:** Ambos tipos de procesos entran al **Módulo encargado de administrar creación e ingreso de procesos**, el cual se encarga de darles de alta en el sistema.

#### 2. Clasificación y Colas de PCB
Una vez admitidos por el módulo de administración, los bloques de control de procesos (PCB) se derivan a dos estructuras de almacenamiento:
* **Cola de PROCESOS PCB:** Registro global o almacén total de los bloques de control de todos los procesos del simulador.
* **Cola de Listos PCB:** Línea de espera específica para los procesos que están en condiciones óptimas para competir por tiempo de ejecución.

#### 3. Núcleo de Planificación y Asignación (Scheduler & Dispatcher)
Representado por el bloque central segmentado en azul, este subcomponente interactúa bidireccionalmente con la Cola de Listos:
* **Módulo encargado de planificar (Scheduler):** Determina qué proceso de la Cola de Listos debe ser el siguiente en ejecutarse según las políticas establecidas.
* **Módulo encargado de otorgar CPU (Dispatcher):** Realiza el cambio de contexto cargando el Program Counter (PC) y despacha el proceso seleccionado directamente hacia la unidad de procesamiento.

#### 4. Ejecución (CPU)
* **CPU:** Ejecuta de forma secuencial las instrucciones del proceso en la dirección de memoria física indicada por su **Program Counter (PC)**.

#### 5. Gestión de Eventos, Interrupciones y Señales
Durante la ejecución o ciclo del proceso, pueden dispararse distintos eventos que alteran su flujo:
* **Eventos de varios (TIMER, Aborts, otros):** La CPU o el propio entorno pueden generar interrupciones por fin de quantum (Timer), cancelaciones (Aborts) u otras excepciones.
* **Cola de eventos E/S:** Si el proceso requiere comunicarse con un periférico (teclado, disco, impresora), abandona la ejecución y pasa a esta cola de espera.
* **Módulo encargado de administrar señales:** Coordina las respuestas a estas interrupciones. Recibe las señales procedentes de la cola de E/S o de los eventos varios, procesa el cambio de estado correspondiente y reubica el proceso de vuelta en la **Cola de Listos PCB** cuando la interrupción ha sido atendida.


---

### Descripción del Diagrama: Proceso para creación de programa ejecutable

Este diagrama ilustra las fases de transformación secuencial por las que atraviesa un programa, desde que se escribe su código fuente hasta que se convierte en un archivo ejecutable en el sistema.

#### Fase 1: Edición
* **Entrada:** **Programa Código Fuente**. Es el archivo de texto plano escrito por el programador en un lenguaje de programación específico.
* **Componente:** El **Editor** es la herramienta o entorno de desarrollo donde se redacta y modifica el código.
* **Herramientas de Apoyo:** Intervienen **Aplicaciones** complementarias del entorno durante la escritura.

#### Fase 2: Traducción
* **Entrada:** Toma el código fuente estructurado en la fase anterior.
* **Componente:** El **Compilador** procesa el código fuente, valida la sintaxis y lo traduce a un lenguaje de bajo nivel.
* **Resultado:** Genera el **Programa Objeto**, que contiene instrucciones en código de máquina o código intermedio, pero que aún no es directamente ejecutable por sí solo.

#### Fase 3: Enlace
* **Entrada:** Toma el **Programa Objeto** generado por el compilador.
* **Componente:** El **L-Editor (Linker / Enlazador)** se encarga de empaquetar y unificar los diferentes módulos del programa.
* **Recursos Adicionales:** El enlazador integra referencias externas necesarias provenientes de **Bibliotecas** (librerías del sistema o de terceros) y otras **Utilidades** requeridas para el funcionamiento del software.
* **Resultado Final:** Produce el **Programa Ejecutable**, el archivo binario final que el sistema operativo puede cargar directamente en memoria para que la CPU inicie su ejecución.