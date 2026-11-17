# Agente local para la detección de botnets y criptominería

Proyecto de graduación 2026 de **André Marroquín Tarot**, carné **22266**.

## Descripción

Desarrollé este prototipo académico para observar procesos, uso de CPU y memoria, y actividad TCP básica en un equipo personal con Windows 11. El agente construye una línea base del comportamiento habitual y compara con ella las mediciones posteriores para identificar desviaciones compatibles con criptominería o actividad de botnets simuladas.

El agente observa, analiza, registra y alerta; una alerta no confirma por sí sola una infección. No elimina archivos ni bloquea procesos. Tampoco reemplaza al antivirus o a un EDR.

## Tecnologías utilizadas

- C# y .NET 10.
- Blazor Hybrid para la interfaz.
- WPF como contenedor de la ventana local.
- SQLite mediante `Microsoft.Data.Sqlite` para el almacenamiento local.
- APIs nativas de Windows para CPU, memoria y procesos.
- Conteo básico de conexiones TCP por proceso.
- Arquitectura x64 para Windows 11.

El procesamiento y el almacenamiento son locales. La aplicación no requiere servicios en la nube, cuentas ni APIs externas. Algunas propiedades de procesos protegidos pueden no estar disponibles sin permisos adicionales.

## Requisitos previos

- Windows 11 x64.
- SDK de .NET 10 para compilar y ejecutar el código fuente.
- Microsoft Edge WebView2 Runtime para mostrar la interfaz Blazor Hybrid.
- No se necesitan Node.js, credenciales ni conexión a servicios externos.

## Estructura

```text
/
  capturas/
    captura-01.png
    captura-02.png
    captura-03.png
    captura-04.png
    captura-05.png
    captura-06.png
    captura-07.png
    captura-08.png
    captura-09.png
    captura-10.png
    captura-11.png
    captura-12.png
  demo/
    demo.mp4
  docs/
    informe_final.pdf
    consentimiento_firmado.pdf
  src/
    .env.example
    BotnetMiningDetector.App/
      Agent/
      Baseline/
      Common/
      Data/
      Detection/
      Evaluation/
      Export/
      Simulation/
      UI/
      wwwroot/
      appsettings.json
      BotnetMiningDetector.App.csproj
    BotnetMiningDetector.Simulator/
      Program.cs
      BotnetMiningDetector.Simulator.csproj
  .gitignore
  BotnetMiningDetector.slnx
  LICENSE
  README.md
```

El informe, el consentimiento firmado, doce capturas numeradas y el video están en las carpetas indicadas. `package.json` **no aplica** porque el proyecto no usa Node.js. Tampoco hace falta crear un archivo `.env`: `src/.env.example` deja constancia de que no se requieren variables de entorno ni credenciales. La configuración del agente se encuentra en `src/BotnetMiningDetector.App/appsettings.json` y en la interfaz.

## Qué mide

El agente recolecta información técnica disponible con permisos normales:

- CPU total y CPU por proceso.
- RAM utilizada por el sistema y por proceso.
- Procesos activos, nombre, PID, ruta y tiempo de vida.
- Procesos nuevos que no aparecen en la línea base.
- Cantidad básica de conexiones TCP por proceso.

Las mediciones se toman en intervalos configurados y se guardan en SQLite.

## Línea base

La línea base representa el comportamiento habitual del equipo. Incluye medias, desviaciones estándar, rangos típicos, procesos habituales y consumo esperado por proceso.

Se guarda en:

```text
agent_monitor.db
```

Al ejecutar desde este repositorio, la base se encuentra en su raíz y se crea automáticamente si no existe. También se puede consultar la ruta efectiva desde la interfaz. La línea base se conserva entre cierres y reinicios; se reconstruye cuando el usuario lo solicita.

Durante su construcción no se generan alertas definitivas.

## Detección y alertas

El motor compara las mediciones actuales con la línea base y aplica:

- Umbrales estadísticos de CPU y memoria.
- Umbrales absolutos para cargas elevadas.
- Reglas heurísticas para procesos nuevos, persistentes o ubicados en rutas inusuales.
- Detección de actividad TCP periódica local.
- Correlación cuando coinciden varias señales.

Las condiciones sostenidas durante el tiempo configurado tienen mayor relevancia que un aumento momentáneo. Las alertas indican un comportamiento sospechoso o inusual, no confirman la existencia de malware.

Cada alerta conserva el proceso involucrado, recurso afectado, regla, severidad, valores observados, referencia de la línea base, duración, evidencia y recomendación.

## Flujo de uso

1. Abrir manualmente la aplicación.
2. Construir la línea base durante el tiempo configurado.
3. Iniciar el monitoreo.
4. Revisar las métricas actuales y los procesos observados.
5. Consultar las alertas y su evidencia.
6. Ejecutar simulaciones seguras desde la sección de validación.
7. Revisar TP, TN, FP, FN, precisión, sensibilidad, tasa de falsos positivos y tiempo de detección.
8. Exportar los resultados a CSV.

La aplicación funciona únicamente mientras está abierta y el monitoreo está activo desde la interfaz.

## Simulaciones seguras

La aplicación incluye comportamientos controlados para evaluar la detección sin utilizar malware real ni infraestructura externa:

- Uso normal.
- Carga legítima temporal.
- CPU sostenida.
- Proceso persistente.
- Actividad periódica hacia localhost.
- Nombre o ruta inusual.
- Escenario combinado.

Cada ejecución registra su inicio, fin, duración, resultado esperado, resultado obtenido, alertas y clasificación.

Los simuladores no minan criptomonedas, no infectan máquinas virtuales y no se conectan a infraestructura maliciosa. Solo reproducen señales observables en un entorno de laboratorio.

## Instalación y ejecución

En Windows PowerShell, clone el fork de entrega y entre al proyecto:

```powershell
git clone https://github.com/mar22266/2026-22266-marroquin-tarot-andre.git
cd .\2026-22266-marroquin-tarot-andre
```

Si ya tiene el proyecto en su equipo, abra PowerShell en la raíz donde se encuentra `BotnetMiningDetector.slnx`. Después ejecute:

```powershell
dotnet restore .\BotnetMiningDetector.slnx
dotnet build .\BotnetMiningDetector.slnx
dotnet run --project .\src\BotnetMiningDetector.App\BotnetMiningDetector.App.csproj
```

La base SQLite se inicializa al abrir la aplicación. Primero construya la línea base y luego inicie el monitoreo desde el dashboard. En **Validación**, seleccione un escenario, espere su ventana de observación y use **Detener simulación** si desea terminarlo antes. El escenario **Uso normal** solo registra la actividad habitual del equipo; tampoco ejecuta malware. Revise las alertas en su sección correspondiente y exporte los CSV desde la aplicación. No hay que copiar ni configurar `.env.example`.

## Datos y exportación

SQLite almacena las mediciones del sistema, procesos, línea base, alertas, escenarios y métricas de evaluación.

### Tablas SQLite

- `SystemMetric`
- `ProcessMetric`
- `BaselineProfile`
- `ProcessBaseline`
- `Alert`
- `ValidationScenario`
- `EvaluationSummary`

### Archivos CSV exportados

La exportación genera archivos con nombres como:

- `metricas_sistema.csv`
- `metricas_procesos.csv`
- `alertas.csv`
- `escenarios_validacion.csv`
- `resumen_evaluacion.csv`

La base de datos y los resultados generados no deben subirse al repositorio. El código fuente, la configuración y la documentación sí forman parte de la aplicación.

## Entregables

- Capturas del sistema: [capturas/](capturas/) (doce PNG numerados de `captura-01` a `captura-12`).
- Video demostrativo: [demo/demo.mp4](demo/demo.mp4).
- Informe final: [docs/informe_final.pdf](docs/informe_final.pdf).
- Consentimiento firmado obligatorio: [docs/consentimiento_firmado.pdf](docs/consentimiento_firmado.pdf).

El [manual oficial](https://csuvg.github.io/Guia-entrega-trabajo-graduacion/docs/manual.html) y el [checklist de entrega](https://csuvg.github.io/Guia-entrega-trabajo-graduacion/docs/checklist.html) describen los requisitos de publicación. La entrega se formaliza mediante un Pull Request desde el [fork personal](https://github.com/mar22266/2026-22266-marroquin-tarot-andre) hacia el [repositorio asignado](https://github.com/GraduacionesUVG/2026-22266-marroquin-tarot-andre).

## Limitaciones

- Algunas propiedades de procesos protegidos pueden aparecer como no disponibles.
- La CPU por proceso se calcula comparando muestras consecutivas.
- La red se analiza mediante conteos básicos de conexiones TCP.
- No se inspeccionan paquetes ni tráfico cifrado.
- No se realizan acciones automáticas sobre procesos o archivos.

## Autor

André Marroquín Tarot, carné 22266.

## Licencia

El código fuente de `src/` se distribuye bajo la [licencia MIT](LICENSE), © 2026 André Marroquín Tarot. Esta autorización no se extiende al informe final, el consentimiento firmado, las capturas ni el video de demostración. Las dependencias de terceros mantienen sus propias licencias.
