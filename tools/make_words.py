# Writes assets/words.txt: the common English words the flipper checks against.
# Needs: pip install wordfreq  (its data is CC BY-SA 4.0, see the header it writes)
import os
import re

from wordfreq import top_n_list

SHAPE = re.compile(r"^[a-z]+(?:'[a-z]+)?$")
SHORT_CONTRACTIONS = {"i'm", "i'd", "i'll", "i've"}

words = []
for rank, word in enumerate(top_n_list("en", 40000)):
    if not SHAPE.match(word):
        continue
    head = word.split("'")[0]
    if "'" in word:
        if len(head) < 2 and word not in SHORT_CONTRACTIONS:
            continue
    elif len(word) < 3:
        continue
    elif len(word) == 3 and rank >= 5000:
        # Rare three-letter "words" are mostly abbreviations, and a short one is
        # likelier to collide with an Arabic word typed on the English layout.
        continue
    words.append(word)

path = os.path.join(os.path.dirname(__file__), "..", "assets", "words.txt")
with open(path, "w", encoding="utf-8", newline="\n") as f:
    f.write("# Common English words from wordfreq (https://github.com/rspeer/wordfreq), CC BY-SA 4.0. Made by tools/make_words.py.\n")
    f.write("\n".join(words) + "\n")
print(len(words), "words")
