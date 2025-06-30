from body import BodyThread
import time
import struct
import global_vars
from sys import exit

thread = BodyThread()
thread.start()

try:
    input("Presiona ENTER o CTRL+C para salir...\n")
except KeyboardInterrupt:
    print("\nInterrupción detectada, cerrando servidor...")

print("Exiting…")        
global_vars.KILL_THREADS = True

# Espera a que el hilo termine
thread.join()

exit()