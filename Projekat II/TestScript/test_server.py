import threading
import requests
import time

# Podesavanja adrese
SERVER_URL = "http://localhost:5050/"
# Fajlovi koji moraju postojati u Files folderu
FILES_TO_TEST = ["test.bin", "proba.txt", "ints.bin", "PodaciOStudentima.txt", "large.bin"]

def simulate_user(user_id, file_name):
    print(f"[User {user_id}] Zahteva fajl: {file_name}")
    try:
        start_time = time.time()
        response = requests.get(SERVER_URL + file_name)
        duration = time.time() - start_time
        
        if response.status_code == 200:
            print(f"[User {user_id}] USPEH | Fajl: {file_name} | Vreme: {duration:.2f}s | Velicina: {len(response.content)} bajtova")
        else:
            print(f"[User {user_id}] GRESKA | Status: {response.status_code} | Poruka: {response.text}")
            
    except Exception as e:
        print(f"[User {user_id}] SERVER NEDOSTUPAN: {e}")

def run_simulation():
    threads = []
    
    # 1. Par korisnika traži ISTI fajl istovremeno (Testiranje kesa/konkurentnog citanja)
    for i in range(150):
        t = threading.Thread(target=simulate_user, args=(i, "large.bin"))
        threads.append(t)
        
    # 2. Par korisnika trazi RAZLICITE fajlove
    for i in range(3, 6):
        file_to_get = FILES_TO_TEST[i % len(FILES_TO_TEST)]
        t = threading.Thread(target=simulate_user, args=(i, file_to_get))
        threads.append(t)

    # Pokretanje svih niti odjednom
    print("--- Pokretanje simulacije ---")
    for t in threads:
        t.start()

    # Cekanje da svi zavrse
    for t in threads:
        t.join()
    print("--- Simulacija zavrsena ---")

if __name__ == "__main__":
    run_simulation()