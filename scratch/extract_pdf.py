import pypdf

reader = pypdf.PdfReader("Tor_GameCard (Update).pdf")
text = ""
for i, page in enumerate(reader.pages):
    text += f"--- Page {i+1} ---\n"
    text += page.extract_text() or ""
    text += "\n\n"

with open("scratch/tor_text.txt", "w", encoding="utf-8") as f:
    f.write(text)

print("Text extracted successfully to scratch/tor_text.txt")
