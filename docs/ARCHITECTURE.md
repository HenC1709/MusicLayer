# Arquitectura inicial

## Flujo de dependencias

```text
Presentation.Wpf → Application → Domain
Infrastructure → Application + Domain
Presentation.Wpf → Infrastructure (composition root)
```

Domain contiene reglas independientes del framework. Application organiza los casos de uso y define los contratos. Infrastructure implementará los contratos de audio, sistema de archivos, metadatos y SQLite. WPF será responsable de la interacción con el usuario y la composición de dependencias.

## SQLite local

La base SQLite se almacenará bajo el directorio de datos local del usuario en una carpeta `MusicLayer`; no se guardará dentro del repositorio ni junto al ejecutable.

SQLite se implementará en el Sprint 4. El esquema se definirá entonces para guardar preferencias como la última carpeta y el volumen; se decidirá si también conviene persistir la biblioteca. Durante el MVP la biblioteca se mantendrá en memoria. Las entidades de dominio no representarán directamente filas de la base de datos.

## Audio y concurrencia

La interfaz de reproducción prevista será asíncrona para evitar bloquear WPF. La implementación deberá liberar el recurso de audio anterior al cambiar de canción y mantener la interacción con controles de UI en el hilo correspondiente.
