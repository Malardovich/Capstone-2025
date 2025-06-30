# Uso interno, no modificar manualmente.
KILL_THREADS = False

# Activa esto para ver cómo la webcam está siendo interpretada (reduce el rendimiento).
DEBUG = True 

# Cambia la configuración de conexión UDP (debe coincidir con la de Unity)
USE_LEGACY_PIPES = False # Solo soportado en Windows (si es True, usa NamedPipes en vez de sockets UDP)
HOST = '127.0.0.1'
PORT = 52733

# Estas configuraciones no se aplican universalmente, no todas las webcams soportan todos los valores de resolución y FPS
CAM_INDEX = 0 # Índice de la webcam en OpenCV2, cambia este valor para usar otra cámara (por ejemplo, una externa).
USE_CUSTOM_CAM_SETTINGS = False
FPS = 60
WIDTH = 320
HEIGHT = 240

# [0, 2] Números más altos son más precisos, pero consumen más recursos. El video de demostración usó 2 (un buen entorno es más importante).
MODEL_COMPLEXITY = 2