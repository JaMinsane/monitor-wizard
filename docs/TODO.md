Se parte de la base de un script de python que funciona:

```python
import wmi
import time
import warnings
from monitorcontrol import get_monitors

warnings.filterwarnings("ignore", category=SyntaxWarning)

# ==========================================
# CONFIGURACIÓN DEL SMART KVM
# ==========================================
DEVICE_KEYWORD = "Samson"
TARGET_INPUT = 15 # Recuerda: 15 para PC (DisplayPort), 5 para Laptop

def set_monitor_input():
    try:
        monitors = get_monitors()
        # Iteramos sobre todos los monitores detectados
        for i, target_monitor in enumerate(monitors):
            with target_monitor:
                try:
                    target_monitor.set_input_source(TARGET_INPUT)
                    print(f"[OK] Monitor {i + 1} cambiado a la entrada {TARGET_INPUT}")
                except Exception as e:
                    print(f"[ERROR] No se pudo cambiar el Monitor {i + 1}: {e}")
                    pass 
    except Exception as e:
        print(f"Error al obtener los monitores: {e}")
        pass

def listen_usb_events():
    c = wmi.WMI()
    watcher = c.watch_for(
        notification_type="Creation",
        wmi_class="Win32_PnPEntity",
        delay_secs=1 # Reducido a 1 segundo para mayor responsividad del hilo
    )

    last_trigger_time = 0
    print(f"Escuchando conexión del dispositivo USB: '{DEVICE_KEYWORD}'...")

    while True:
        try:
            new_device = watcher()
            device_name = new_device.Name if new_device.Name else ""
            
            if DEVICE_KEYWORD.lower() in device_name.lower():
                current_time = time.time()
                
                # Debounce agresivo: Solo 2.5 segundos de bloqueo
                if (current_time - last_trigger_time) > 2.5:
                    print(f"\n[!] Dispositivo '{device_name}' detectado. Cambiando monitores...")
                    
                    # Micro-pausa de 200ms solo para asegurar la estabilidad del bus USB
                    time.sleep(0.2) 
                    
                    # Disparo inmediato a todos los monitores
                    set_monitor_input()
                    
                    last_trigger_time = time.time()
        except Exception as e:
            time.sleep(1)

if __name__ == "__main__":
    listen_usb_events()
```

Se requiere migrar en fases a una aplicación WinUI con C# que tenga la misma funcionalidad pero con una interfaz gráfica y opciones de configuración, incluyendo un icono en la bandeja del sistema para control manual y automático.

Fase 1:
- App WinUI con pantalla simple.
- Botón "Switch to PC / DisplayPort".
- Botón "Switch to Laptop / HDMI".
- Shortcut global para switch manual: Ctrl + Alt + F12 → Toggle PC/Laptop.
- Log visual de resultados.

Fase 2:
- Agregar watcher WMI para detectar USB.
- Debounce de 2.5 segundos.
- Ejecutar cambio automático.

Fase 3:
- Agregar tray icon.
- Menú:
  - Open Monitor Wizard
  - Switch to PC
  - Switch to Laptop
  - Auto switch enabled
  - Exit
- Presionar X en la ventana principal minimizará a la bandeja al tray icon, no cerrará la app.

Notas importantes:
Esta app usa WinUI 3 mandatoriamente y esta orientada a Windows 11.
Ya se tiene instalado el paquete System.Management. Si se requiere agregar otros paquetes, estan disponibles en NuGet
