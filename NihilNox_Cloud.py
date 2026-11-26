import sys
import os
import shutil
import subprocess
import json
import uuid
import yt_dlp
import uvicorn
from fastapi import FastAPI, HTTPException, BackgroundTasks, Header, Depends
from fastapi.responses import FileResponse
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel

if sys.platform == "win32":
    try:
        sys.stdout.reconfigure(encoding='utf-8')
        sys.stderr.reconfigure(encoding='utf-8')
    except:
        pass

# ── Configuración persistente ───────────────────────────────
CONFIG_FILE  = os.path.join(os.path.expanduser("~"), ".nihilnox_config.json")
COOKIES_FILE = os.path.join(os.path.expanduser("~"), ".nihilnox_cookies.txt")

def cargar_config():
    try:
        if os.path.exists(CONFIG_FILE):
            with open(CONFIG_FILE, "r") as f:
                return json.load(f)
    except:
        pass
    return {}

_config = cargar_config()
API_KEY     = _config.get("api_key", "MI_KEY_SECRETA")
PORT        = 8000
FFMPEG_PATH = _config.get("ffmpeg_path", "ffmpeg")

app = FastAPI(title="NihilNox Headless Server")

# Habilitar CORS para permitir peticiones desde la App Android sin bloqueos
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# ── Seguridad ───────────────────────────────────────────────
async def verify_token(authorization: str = Header(None, alias="Authorization")):
    if not API_KEY:
        return
    token = authorization.replace("Bearer ", "").replace("bearer ", "").strip() if authorization else ""
    if token != API_KEY:
        raise HTTPException(status_code=401, detail="API key inválida")

class DescargaRequest(BaseModel):
    url: str

class BuscarCancionRequest(BaseModel):
    query: str

def limpiar_archivo(path: str):
    try:
        if path and os.path.exists(path):
            os.remove(path)
            print(f"🗑️ Eliminado temporal: {path}")
    except Exception as e:
        print(f"⚠️ Error limpiando: {e}")

# ── Endpoints ───────────────────────────────────────────────

@app.get("/")
async def root():
    return {"status": "online", "mode": "headless-server"}

@app.get("/health")
def health():
    return {"status": "ok"}

@app.post("/download-music")
async def download_music(payload: dict, background_tasks: BackgroundTasks, _ = Depends(verify_token)):
    url = payload.get("url")
    if not url:
        raise HTTPException(status_code=400, detail="URL missing")

    temp_id = str(uuid.uuid4())
    output = f"music_{temp_id}.mp3"
    print(f"🎵 [DOWNLOAD-MUSIC] Solicitado: {url[:60]}...")

    ydl_opts = {
        'format': 'bestaudio/best',
        'outtmpl': output,
        'quiet': False,
        'no_warnings': False,
        'socket_timeout': 30,
        'noplaylist': True,
        'ffmpeg_location': FFMPEG_PATH if os.path.exists(FFMPEG_PATH) else None,
        'postprocessors': [{
            'key': 'FFmpegExtractAudio',
            'preferredcodec': 'mp3',
            'preferredquality': '192',
        }],
        'http_headers': {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36'
        }
    }
    if os.path.exists(COOKIES_FILE):
        ydl_opts['cookiefile'] = COOKIES_FILE

    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            ydl.extract_info(url, download=True)

        archivo_encontrado = None
        for f in os.listdir("."):
            if f.startswith(f"music_{temp_id}") and f.endswith(".mp3"):
                archivo_encontrado = f
                break

        if not archivo_encontrado or not os.path.exists(archivo_encontrado):
            raise Exception("No se pudo generar el archivo MP3 de audio")

        size = os.path.getsize(archivo_encontrado)
        print(f"✅ [DOWNLOAD-MUSIC] Listo: {archivo_encontrado} ({size // 1024} KB)")
        background_tasks.add_task(limpiar_archivo, archivo_encontrado)
        return FileResponse(path=archivo_encontrado, media_type='audio/mpeg', filename="music.mp3")

    except Exception as e:
        print(f"❌ [DOWNLOAD-MUSIC] Error: {str(e)}")
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/download-instagram")
async def download_instagram(payload: dict, background_tasks: BackgroundTasks, _ = Depends(verify_token)):
    url = payload.get("url")
    if not url: raise HTTPException(status_code=400, detail="URL missing")
    output = f"video_{uuid.uuid4()}.mp4"
    print(f"🎬 [INSTAGRAM] Descargando: {url[:60]}...")

    ydl_opts = {
        'format': 'best[ext=mp4]/best[ext=webm]/best',
        'outtmpl': output,
        'quiet': False,
        'no_warnings': False,
        'socket_timeout': 30,
        'http_headers': {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36',
        },
        'extractor_args': {
            'instagram': {
                'android_client_id': 'com.instagram.android',
            }
        }
    }
    if os.path.exists(COOKIES_FILE):
        ydl_opts['cookiefile'] = COOKIES_FILE

    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            ydl.extract_info(url, download=True)
        if not os.path.exists(output):
            raise Exception("El archivo de video no se creó")
        print(f"✅ [INSTAGRAM] Video listo: {output}")
        background_tasks.add_task(limpiar_archivo, output)
        return FileResponse(path=output, media_type='video/mp4', filename="video.mp4")
    except Exception as e:
        print(f"❌ [INSTAGRAM] Error: {str(e)}")
        if os.path.exists(output):
            try: os.remove(output)
            except: pass
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/descargar")
def descargar(req: DescargaRequest, _ = Depends(verify_token)):
    print(f"🎵 Descargar playlist solicitado: {req.url[:60]}...")
    if os.path.exists("musica"): shutil.rmtree("musica")
    os.makedirs("musica", exist_ok=True)

    if "spotify.com" in req.url:
        try:
            try:
                subprocess.run(["py", "-3.12", "-m", "spotdl", req.url, "--output", "musica/{list-name}/{title}"], timeout=600, check=True)
            except:
                subprocess.run(["spotdl", req.url, "--output", "musica/{list-name}/{title}"], timeout=600, check=True)
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Error spotdl: {e}")

        resultados = []
        nombre_playlist = "Spotify"
        for root, _, files in os.walk("musica"):
            for f in files:
                if f.endswith((".mp3", ".m4a", ".opus")):
                    resultados.append({"titulo": os.path.splitext(f)[0], "ruta": os.path.join(root, f)})
                    if nombre_playlist == "Spotify":
                        nombre_playlist = os.path.basename(root)
        return {"playlist": nombre_playlist, "canciones": resultados, "total": len(resultados), "fallidos": []}
    else:
        ydl_opts = {
            "format": "bestaudio/best",
            "outtmpl": "musica/%(playlist_title,title)s/%(title)s.%(ext)s",
            "ignoreerrors": True,
            "ffmpeg_location": FFMPEG_PATH if os.path.exists(FFMPEG_PATH) else None
        }
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            info = ydl.extract_info(req.url, download=True)

        resultados = []
        for root, _, files in os.walk("musica"):
            for f in files:
                if f.endswith((".m4a", ".webm", ".mp3")):
                    resultados.append({"titulo": os.path.splitext(f)[0], "ruta": os.path.join(root, f)})
        return {"playlist": info.get("title", "YouTube"), "canciones": resultados, "total": len(resultados), "fallidos": []}

@app.post("/buscar-cancion")
def buscar_cancion(req: BuscarCancionRequest, _ = Depends(verify_token)):
    print(f"🔍 Buscando canción: {req.query[:60]}")
    carpeta = "busquedas"
    os.makedirs(carpeta, exist_ok=True)
    for f in os.listdir(carpeta):
        try:
            os.remove(os.path.join(carpeta, f))
        except:
            pass

    query = req.query if "http" in req.query else f"{req.query} official audio"
    ydl_opts = {
        "format": "bestaudio/best",
        "outtmpl": f"{carpeta}/%(title)s.%(ext)s",
        "default_search": "ytsearch1",
        "noplaylist": True,
        "ffmpeg_location": FFMPEG_PATH if os.path.exists(FFMPEG_PATH) else None,
        "postprocessors": [{"key": "FFmpegExtractAudio", "preferredcodec": "mp3", "preferredquality": "192"}],
    }
    try:
        with yt_dlp.YoutubeDL(ydl_opts) as ydl:
            ydl.download([query])
        for f in os.listdir(carpeta):
            return {"titulo": os.path.splitext(f)[0], "ruta": os.path.join(carpeta, f)}
        raise HTTPException(status_code=404, detail="No encontrada")
    except Exception as e:
        print(f"❌ Error buscando: {e}")
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/info-cancion")
def info_cancion(req: DescargaRequest, _ = Depends(verify_token)):
    try:
        try:
            res = subprocess.run(["py", "-3.12", "-m", "spotdl", "meta", req.url], capture_output=True, text=True, timeout=30)
        except:
            res = subprocess.run(["spotdl", "meta", req.url], capture_output=True, text=True, timeout=30)

        titulo = ""; artista = ""
        for line in res.stdout.splitlines():
            if line.lower().startswith("title:"): titulo = line.split(":", 1)[1].strip()
            elif line.lower().startswith("artist:"): artista = line.split(":", 1)[1].strip()
        if titulo and artista: return {"query": f"{artista} - {titulo}"}
        raise HTTPException(status_code=404, detail="No info found")
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))

@app.get("/archivo")
def obtener_archivo(ruta: str, authorization: str = Header(None, alias="Authorization")):
    if API_KEY:
        token = authorization.replace("Bearer ", "").replace("bearer ", "").strip() if authorization else ""
        if token != API_KEY: raise HTTPException(status_code=401, detail="API key inválida")

    if not os.path.exists(ruta): raise HTTPException(status_code=404, detail="No existe")
    return FileResponse(path=ruta, media_type='audio/mpeg', filename=os.path.basename(ruta))

if __name__ == "__main__":
    print(f"🚀 Iniciando servidor headless en puerto {PORT}...")
    uvicorn.run(app, host="0.0.0.0", port=PORT, log_level="info")
