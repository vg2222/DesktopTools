"""Pin compatible OPUS-MT ONNX packs and their checksums. No weights are downloaded.

Run manually when reviewing new pack versions; the application uses the committed catalog.
"""
import concurrent.futures
import hashlib
import json
import pathlib
import re
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[1]
HOST = "https://huggingface.co"
FILES = ["config.json", "source.spm", "vocab.json", "README.md",
         "onnx/encoder_model_quantized.onnx", "onnx/decoder_model_merged_quantized.onnx"]


def read(url):
    with urllib.request.urlopen(url, timeout=60) as response:
        return response.read()


def pack(direction):
    repo = "Xenova/opus-mt-" + direction
    metadata = json.loads(read(f"{HOST}/api/models/{repo}?blobs=true"))
    upstream = "Helsinki-NLP/opus-mt-" + direction
    original = json.loads(read(f"{HOST}/api/models/{upstream}"))
    license_id = original.get("cardData", {}).get("license", "")
    if license_id not in ("cc-by-4.0", "apache-2.0"):
        raise ValueError("Unsupported or missing upstream license: " + license_id)
    revision = metadata["sha"]
    by_name = {item["rfilename"]: item for item in metadata["siblings"]}
    config = json.loads(read(f"{HOST}/{repo}/resolve/{revision}/config.json"))
    if (config.get("model_type") != "marian" or config.get("eos_token_id") != 0
            or not config.get("share_encoder_decoder_embeddings", False)):
        raise ValueError("Model needs a different tokenizer or decoder")
    files = []
    for name in FILES:
        item = by_name[name]
        url = f"{HOST}/{repo}/resolve/{revision}/{name}"
        checksum = item.get("lfs", {}).get("sha256")
        if checksum is None:
            checksum = hashlib.sha256(read(url)).hexdigest()
        files.append({"File": name.removeprefix("onnx/"), "Url": url,
                      "Sha256": checksum.upper(), "Bytes": item["size"]})
    return {"Direction": direction, "Repository": repo, "Revision": revision,
            "Upstream": upstream, "License": license_id, "Files": files}


def main():
    models = json.loads(read(f"{HOST}/api/models?author=Xenova&search=opus-mt-&limit=500"))
    directions = sorted({item["id"].removeprefix("Xenova/opus-mt-") for item in models
                         if re.fullmatch(r"Xenova/opus-mt-(en-[a-z]{2}|[a-z]{2}-en)", item["id"])})
    languages = {code for direction in directions for code in direction.split("-") if code != "en"}
    directions = [direction for direction in directions if any(
        direction in (f"en-{code}", f"{code}-en") and f"en-{code}" in directions
        and f"{code}-en" in directions for code in languages)]
    packs = []
    with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
        futures = {pool.submit(pack, direction): direction for direction in directions}
        for future in concurrent.futures.as_completed(futures):
            direction = futures[future]
            try:
                packs.append(future.result())
                print("Pinned", direction, flush=True)
            except Exception as error:
                print("Skipped", direction, str(error), flush=True)
    supported = {item["Direction"] for item in packs}
    packs = [item for item in packs if "-".join(reversed(item["Direction"].split("-"))) in supported]
    path = ROOT / "src/DesktopTools/Assets/Translation/catalog.json"
    path.write_text(json.dumps(sorted(packs, key=lambda item: item["Direction"]),
                               ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Saved {len(packs)} pinned packs to {path.name}")


if __name__ == "__main__":
    main()
