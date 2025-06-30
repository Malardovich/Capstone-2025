import mediapipe as mp
import cv2
import json
import math
import sys
import os
import time

# Configuración
VIDEO_PATH = "T13 test.mp4"      # Cambia por la ruta de tu video
OUTPUT_JSON = "movimientos2.json"
FINISHED_VIDEO_PATH = "video_finalizado.mp4"  # Cambia por el video que quieres mostrar al final

mp_pose = mp.solutions.pose
mp_hands = mp.solutions.hands

# Mapeo de nombres para landmarks corporales
BODY_LANDMARK_NAMES = [
    "NOSE", "LEFT_EYE_INNER", "LEFT_EYE", "LEFT_EYE_OUTER",
    "RIGHT_EYE_INNER", "RIGHT_EYE", "RIGHT_EYE_OUTER",
    "LEFT_EAR", "RIGHT_EAR", "MOUTH_LEFT", "MOUTH_RIGHT",
    "LEFT_SHOULDER", "RIGHT_SHOULDER", "LEFT_ELBOW", "RIGHT_ELBOW",
    "LEFT_WRIST", "RIGHT_WRIST", "LEFT_PINKY", "RIGHT_PINKY",
    "LEFT_INDEX", "RIGHT_INDEX", "LEFT_THUMB", "RIGHT_THUMB",
    "LEFT_HIP", "RIGHT_HIP", "LEFT_KNEE", "RIGHT_KNEE",
    "LEFT_ANKLE", "RIGHT_ANKLE", "LEFT_HEEL", "RIGHT_HEEL",
    "LEFT_FOOT_INDEX", "RIGHT_FOOT_INDEX"
]

# Mapeo de nombres para landmarks de manos
HAND_LANDMARK_NAMES = [
    "WRIST", "THUMB_CMC", "THUMB_MCP", "THUMB_IP", "THUMB_TIP",
    "INDEX_FINGER_MCP", "INDEX_FINGER_PIP", "INDEX_FINGER_DIP", "INDEX_FINGER_TIP",
    "MIDDLE_FINGER_MCP", "MIDDLE_FINGER_PIP", "MIDDLE_FINGER_DIP", "MIDDLE_FINGER_TIP",
    "RING_FINGER_MCP", "RING_FINGER_PIP", "RING_FINGER_DIP", "RING_FINGER_TIP",
    "PINKY_MCP", "PINKY_PIP", "PINKY_DIP", "PINKY_TIP"
]

def extract_hand_data(hand_landmarks, handedness_label):
    wrist = hand_landmarks.landmark[0]
    middle_base = hand_landmarks.landmark[9]
    base_length = math.sqrt(
        (middle_base.x - wrist.x) ** 2 +
        (middle_base.y - wrist.y) ** 2 +
        (middle_base.z - wrist.z) ** 2
    )
    if base_length < 0.001:
        base_length = 0.001
    scale_factor = 0.15
    hand_data = []
    
    for idx, lm in enumerate(hand_landmarks.landmark):
        rel_x = (lm.x - wrist.x) / base_length * scale_factor
        rel_y = -(lm.y - wrist.y) / base_length * scale_factor  # Invertir eje Y
        rel_z = (lm.z - wrist.z) / base_length * scale_factor
        
        hand_data.append({
            "hand": handedness_label,
            "landmark": idx,
            "name": HAND_LANDMARK_NAMES[idx],
            "x": rel_x,
            "y": rel_y,
            "z": rel_z
        })
    
    return hand_data

def mostrar_video_finalizado(video_path):
    cap = cv2.VideoCapture(video_path)
    if not cap.isOpened():
        print(f"No se pudo abrir el video final: {video_path}")
        return
    print("\nReproduciendo video de finalización...")
    while cap.isOpened():
        ret, frame = cap.read()
        if not ret:
            break
        cv2.imshow("¡PROCESO FINALIZADO!", frame)
        if cv2.waitKey(30) & 0xFF == 27:  # ESC para cerrar antes
            break
    cap.release()
    cv2.destroyAllWindows()

def main():
    cap = cv2.VideoCapture(VIDEO_PATH)
    all_frames = []
    frame_idx = 0

    # Obtener duración del video
    fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = cap.get(cv2.CAP_PROP_FRAME_COUNT)
    duration_sec = total_frames / fps if fps > 0 else 0

    with mp_pose.Pose(
        min_detection_confidence=0.8,
        min_tracking_confidence=0.5,
        model_complexity=1,
        static_image_mode=False,
        enable_segmentation=False
    ) as pose, mp_hands.Hands(
        max_num_hands=2,
        min_detection_confidence=0.7,
        min_tracking_confidence=0.5,
        model_complexity=1
    ) as hands:

        try:
            while cap.isOpened():
                ret, frame = cap.read()
                if not ret:
                    break

                image = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                results_pose = pose.process(image)
                results_hands = hands.process(image)

                # Cuerpo
                body_data = []
                if results_pose.pose_world_landmarks:
                    for i, lm in enumerate(results_pose.pose_world_landmarks.landmark):
                        body_data.append({
                            "index": i,
                            "name": BODY_LANDMARK_NAMES[i],
                            "x": lm.x,
                            "y": lm.y,
                            "z": lm.z
                        })

                # Manos
                hands_data = []
                if results_hands.multi_hand_landmarks and results_hands.multi_handedness:
                    for hand_idx, hand_landmarks in enumerate(results_hands.multi_hand_landmarks):
                        handedness = results_hands.multi_handedness[hand_idx].classification[0].label
                        hand_prefix = "Left" if handedness == "Left" else "Right"
                        hands_data.extend(extract_hand_data(hand_landmarks, hand_prefix))

                # Guardar frame
                all_frames.append({
                    "palabra_predicha": "",
                    "fotograma": frame_idx,
                    "body": body_data,
                    "hands": hands_data
                })
                frame_idx += 1

                # Mostrar avance
                sys.stdout.write(f"\rProcesando frame {frame_idx}")
                sys.stdout.flush()

        except KeyboardInterrupt:
            print("\nInterrumpido por el usuario. Guardando archivo...")

        finally:
            cap.release()
            with open(OUTPUT_JSON, "w") as f:
                json.dump(all_frames, f, indent=2)
            print(f"\nArchivo guardado: {OUTPUT_JSON}")

            # Estadísticas
            minutos = duration_sec / 60 if duration_sec > 0 else frame_idx / (fps * 60) if fps > 0 else 0
            total_coordenadas = sum(len(f["body"]) + len(f["hands"]) for f in all_frames)
            print(f"\n¡PROCESO FINALIZADO!")
            print(f"Duración del video: {minutos:.2f} minutos")
            print(f"Frames procesados: {frame_idx}")
            print(f"Coordenadas generadas: {total_coordenadas}")

            # Mostrar video de finalización si existe
            if os.path.exists(FINISHED_VIDEO_PATH):
                mostrar_video_finalizado(FINISHED_VIDEO_PATH)
            else:
                print(f"(No se encontró el video de finalización: {FINISHED_VIDEO_PATH})")

            print("\n¡Listo! El proceso ha finalizado y los datos han sido guardados.")

if __name__ == "__main__":
    main()