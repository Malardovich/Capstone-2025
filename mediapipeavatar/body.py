import json
import time
import threading
import global_vars
import struct
from clientUDP import ClientUDP

class BodyThread(threading.Thread):
    data = ""
    dirty = True
    pipe = None
    timeSinceCheckedConnection = 0
    timeSincePostStatistics = 0
    current_frame_index = 0
    playback_speed = 1.0  # Velocidad de reproducción (1.0 = tiempo real)
    client = None

    def run(self):
        # Cargar el archivo JSON con los movimientos grabados
        try:
            with open('movimientos2.json', 'r') as f:
                frames = json.load(f)  # Carga directa de la lista de frames
                print(f"Loaded {len(frames)} frames from movimientos.json")
        except Exception as e:
            print(f"Error loading JSON file: {e}")
            return

        self.setup_comms()
        
        # Reproducir los frames
        last_frame_time = time.time()
        
        while not global_vars.KILL_THREADS and self.current_frame_index < len(frames):
            # Controlar velocidad de reproducción
            current_time = time.time()
            if current_time - last_frame_time < (1.0 / 30.0) / self.playback_speed:
                time.sleep(0.001)
                continue
                
            last_frame_time = current_time
            
            # Obtener el frame actual
            frame = frames[self.current_frame_index]
            self.current_frame_index += 1
            
            # Preparar datos para enviar
            self.data = ""
            
            # Procesar datos del cuerpo - Formato 2
            if "body" in frame:
                for landmark in frame["body"]:
                    # Acceder a los valores como diccionario
                    index = landmark["index"]
                    x = landmark["x"]
                    y = landmark["y"]
                    z = landmark["z"]
                    self.data += f"{index}|{x}|{y}|{z}\n"
            
            # Procesar datos de las manos - Formato 2
            if "hands" in frame:
                for hand_landmark in frame["hands"]:
                    # Convertir "Left" a "L" y "Right" a "R"
                    hand_prefix = "L" if hand_landmark["hand"].startswith("L") else "R"
                    idx = hand_landmark["landmark"]
                    x = hand_landmark["x"]
                    y = hand_landmark["y"]
                    z = hand_landmark["z"]
                    self.data += f"FINGER|{hand_prefix}|{idx}|{x}|{y}|{z}\n"
            
            # Enviar datos
            self.send_data(self.data)
            
            # Mostrar progreso
            if self.current_frame_index % 30 == 0:
                print(f"Playing frame {self.current_frame_index}/{len(frames)}")
            
            # Revisa si se debe terminar (por si KILL_THREADS se activa durante el sleep)
            if global_vars.KILL_THREADS:
                break
                
        print("Playback finished.")
        
        # Cerrar comunicación UDP o Pipe
        if not global_vars.USE_LEGACY_PIPES and self.client:
            self.client.stop()  # Debes implementar stop() en ClientUDP para cerrar el hilo y el socket
            self.client.join(timeout=2)
        if global_vars.USE_LEGACY_PIPES and self.pipe:
            self.pipe.close()

    def setup_comms(self):
        if not global_vars.USE_LEGACY_PIPES:
            self.client = ClientUDP(global_vars.HOST, global_vars.PORT)
            self.client.start()
        else:
            print("Using Pipes for interprocess communication.")
    
    def send_data(self, message):
        if not global_vars.USE_LEGACY_PIPES:
            self.client.sendMessage(message)
        else:
            if not self.pipe and time.time() - self.timeSinceCheckedConnection >= 1:
                try:
                    self.pipe = open(r'\\.\pipe\UnityMediaPipeBody1', 'r+b', 0)
                except FileNotFoundError:
                    print("Waiting for Unity project to run...")
                    self.pipe = None
                self.timeSinceCheckedConnection = time.time()

            if self.pipe:
                try:
                    s = message.encode('utf-8')
                    self.pipe.write(struct.pack('I', len(s)) + s)
                    self.pipe.seek(0)
                except Exception as ex:
                    print("Failed to write to pipe:", str(ex))
                    self.pipe = None