import cv2
import mediapipe as mp
import socket
import json
import threading
import signal
import sys
import os
import time  # ¡Este import faltaba!

# Configuración de MediaPipe
mp_hands = mp.solutions.hands
hands = mp_hands.Hands(
    static_image_mode=True,
    max_num_hands=2,
    min_detection_confidence=0.7,
    min_tracking_confidence=0.5
)

# Configuración del servidor
HOST = "127.0.0.1"
PORT = 65432
running = False
IMAGE_PATH = os.path.join("A-H", "img88Escalada2.jpg")

def handle_client(conn, addr):
    print(f"\n🔗 Conexión establecida con {addr}")
    
    try:
        # Cargar imagen
        image = cv2.imread(IMAGE_PATH)
        if image is None:
            error_msg = f"Error: No se encontró la imagen en {IMAGE_PATH}"
            print(error_msg)
            conn.sendall(json.dumps({"error": error_msg}).encode())
            return

        # Procesar imagen
        image_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
        results = hands.process(image_rgb)

        # Preparar datos
        landmarks_data = []
        if results.multi_hand_landmarks:
            print(f"✅ Se detectaron {len(results.multi_hand_landmarks)} manos")
            for hand_landmarks in results.multi_hand_landmarks:
                landmarks = []
                for idx, landmark in enumerate(hand_landmarks.landmark):
                    landmarks.append({
                        "x": landmark.x,
                        "y": landmark.y,
                        "z": landmark.z,
                        "id": idx
                    })
                landmarks_data.append(landmarks)

        # Enviar datos periódicamente
        while running:
            try:
                conn.sendall((json.dumps(landmarks_data) + "\n").encode())
                time.sleep(0.1)  # Ahora funciona correctamente
            except (BrokenPipeError, ConnectionResetError):
                break
                
    except Exception as e:
        print(f"\n❌ Error con {addr}: {str(e)}")
    finally:
        conn.close()
        print(f"🔌 Desconectado de {addr}")

def signal_handler(sig, frame):
    global running
    print("\n🔴 Recibida señal CTRL+C, cerrando servidor...")
    running = False
    sys.exit(0)

def start_server():
    global running
    running = True
    signal.signal(signal.SIGINT, signal_handler)
    
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((HOST, PORT))
        s.listen()
        s.settimeout(1)
        print(f"🖥️ Servidor Python iniciado en {HOST}:{PORT}")
        print(f"📁 Procesando imagen: {IMAGE_PATH}")
        print("Presiona CTRL+C para detener\n")

        while running:
            try:
                conn, addr = s.accept()
                thread = threading.Thread(
                    target=handle_client,
                    args=(conn, addr),
                    daemon=True
                )
                thread.start()
                print(f"📶 Conexiones activas: {threading.active_count() - 1}")
            except socket.timeout:
                continue
            except Exception as e:
                if running:
                    print(f"⚠️ Error: {str(e)}")

if __name__ == "__main__":
    if not os.path.exists(IMAGE_PATH):
        print(f"❌ Error: No se encontró la imagen en {IMAGE_PATH}")
    else:
        start_server()