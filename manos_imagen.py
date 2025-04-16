import cv2
import mediapipe as mp

mp_drawing = mp.solutions.drawing_utils
mp_hands = mp.solutions.hands

with mp_hands.Hands(
    static_image_mode=True,
    max_num_hands=2,
    min_detection_confidence=0.5) as hands:

    image = cv2.imread("A-H\img88Escalada2.jpg")
    height, width, _ = image.shape
    image = cv2.flip(image, 1)

    image_rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)

    results = hands.process(image_rgb)

    # HANDEDNESS
    print("handedness:", results.multi_handedness)
    #HAND LANDMARKS
    print("hand_landmarks:", results.multi_hand_landmarks)

    if results.multi_hand_landmarks is not None:
        #--------------------------------------------------
        # Dibuja los puntos de referencia de la mano y sus conexiones con mediapipe
        for hand_landmarks in results.multi_hand_landmarks:
            print(hand_landmarks)

            mp_drawing.draw_landmarks(
                image, hand_landmarks, mp_hands.HAND_CONNECTIONS,
            )

    image = cv2.flip(image, 1)
cv2.imshow("image", image)
cv2.waitKey(0)
cv2.destroyAllWindows()