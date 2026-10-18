import sys, json, re, math
import pandas as pd
src, out = sys.argv[1], sys.argv[2]
v = pd.read_excel(src, sheet_name=0, header=None)
linhas = v.iloc[4:].copy()
linhas = linhas[pd.to_numeric(linhas[0], errors="coerce").notna()]
linhas["_ordem"] = linhas[2].apply(lambda x: 0 if x == 99 else 1)
linhas = linhas.sort_values([0, "_ordem", 2], kind="stable")  # "não se aplica" primeiro: vira o nome base
ACENTOS = {
 "grao":"grão","graos":"grãos","feijao":"feijão","pao":"pão","paes":"pães","limao":"limão","mamao":"mamão","melao":"melão","camarao":"camarão",
 "acucar":"açúcar","linguica":"linguiça","cafe":"café","maca":"maçã","abobora":"abóbora","pessego":"pêssego","brocolis":"brócolis","cha":"chá",
 "pure":"purê","figado":"fígado","coracao":"coração","file":"filé","macarrao":"macarrão","agua":"água","sanduiche":"sanduíche","hamburguer":"hambúrguer",
 "suina":"suína","suino":"suíno","pimentao":"pimentão","agriao":"agrião","maracuja":"maracujá","acai":"açaí","oleo":"óleo","geleia":"geleia","requeijao":"requeijão",
 "acem":"acém","musculo":"músculo","mocoto":"mocotó","pirao":"pirão","pacoca":"paçoca","pave":"pavê","chimarrao":"chimarrão","cachaca":"cachaça",
 "milanesa":"milanesa","feijoes":"feijões","limoes":"limões","tomate":"tomate","salmao":"salmão","lagosta":"lagosta","caqui":"caqui","jilo":"jiló",
 "quiabo":"quiabo","chuchu":"chuchu","acelga":"acelga","alho":"alho","pepino":"pepino","rucula":"rúcula","espinafre":"espinafre","couve":"couve",
 "mandioquinha":"mandioquinha","inhame":"inhame","batata":"batata","pequi":"pequi","cupuacu":"cupuaçu","graviola":"graviola","acerola":"acerola",
 "amendoa":"amêndoa","amendoas":"amêndoas","nozes":"nozes","avela":"avelã","ervilha":"ervilha","lentilha":"lentilha","grao-de-bico":"grão-de-bico",
 "soja":"soja","pastel":"pastel","coxinha":"coxinha","esfiha":"esfiha","pizza":"pizza","lasanha":"lasanha","nhoque":"nhoque","estrogonofe":"estrogonofe",
 "galinhada":"galinhada","bobo":"bobó","vatapa":"vatapá","acaraje":"acarajé","tacaca":"tacacá","manicoba":"maniçoba","pudim":"pudim","flan":"flã",
 "sorvete":"sorvete","picole":"picolé","bombom":"bombom","achocolatado":"achocolatado","refrigerante":"refrigerante","energetico":"energético",
 "isotonico":"isotônico","cafe-com-leite":"café com leite","organico":"orgânico","integral":"integral","light":"light","diet":"diet",
 "atum":"atum","peixe":"peixe","frances":"francês","doce":"doce","salgado":"salgado","salgada":"salgada","molho":"molho","ovos":"ovos",
 "vegetal":"vegetal","mamao-papaia":"mamão papaia","pera":"pera","maionese":"maionese","margarina":"margarina","manteiga":"manteiga",
 "creme":"creme","leite":"leite","po":"pó","cha-mate":"chá-mate","mate":"mate","guarana":"guaraná","tamara":"tâmara","ameixa":"ameixa",
 "uva-passa":"uva-passa","farofa":"farofa","acougue":"açougue","lombo":"lombo","toucinho":"toucinho","bacon":"bacon","presunto":"presunto",
 "mortadela":"mortadela","salame":"salame","peru":"peru","pato":"pato","codorna":"codorna","carneiro":"carneiro","bode":"bode","cabrito":"cabrito",
 "coelho":"coelho","porco":"porco","boi":"boi","vitela":"vitela","bovina":"bovina","bovino":"bovino","frango":"frango","galinha":"galinha",
 "caldo":"caldo","sopa":"sopa","canjica":"canjica","mingau":"mingau","papa":"papa","vitamina":"vitamina","iogurte":"iogurte","queijo":"queijo",
 "agucar":"açúcar","acucarado":"açucarado","adocado":"adoçado","adocante":"adoçante","nao":"não","sem":"sem","com":"com",
 "cozido":"cozido","cozida":"cozida","assado":"assado","assada":"assada","frito":"frito","frita":"frita","refogado":"refogado","refogada":"refogada",
 "empanado":"empanado","empanada":"empanada","grelhado":"grelhado","grelhada":"grelhada","ensopado":"ensopado","gratinado":"gratinado",
 "a":"à","umido":"úmido","seco":"seco","sao":"são","joao":"joão","acai-na-tigela":"açaí na tigela","cacau":"cacau","viscera":"víscera","visceras":"vísceras","xerem":"xerém","file":"filé","milanesa":"milanesa","balas":"balas",
}
MINUSC = {"de","da","do","das","dos","e","com","sem","em","na","no","ou","a","o","ao","à","p/","para"}
def bonito(t):
    t = re.sub(r"\(A\)|\(AS\)|\(OS\)|\(O\)", "", str(t)).strip()
    t = re.sub(r"\s+", " ", t)
    palavras = []
    for i, w in enumerate(t.split(" ")):
        lw = w.lower()
        m = re.match(r"^([\(\[]?)([^\)\],/]*)([\)\],/]*)$", lw)
        pre, core, pos = (m.group(1), m.group(2), m.group(3)) if m else ("", lw, "")
        core = ACENTOS.get(core, core)
        palavras.append(pre + core + pos)
    s = " ".join(palavras)
    s = s.replace(" a milanesa", " à milanesa")
    return s[:1].upper() + s[1:]
def num(x):
    try:
        f = float(x)
        return 0.0 if math.isnan(f) else round(f, 2)
    except Exception:
        return 0.0
itens = []; vistos = {}
for _, r in linhas.iterrows():
    cod = r[0]
    if not isinstance(cod, (int, float)) or (isinstance(cod, float) and math.isnan(cod)): continue
    cod = int(cod); prep = int(r[2]) if not pd.isna(r[2]) else 99
    vals = (num(r[6]), num(r[7]), num(r[9]), num(r[8]), num(r[10]))  # kcal, prot, carb, gord, fibra
    if vals[0] <= 0 and sum(vals[1:4]) <= 0: continue
    chave = vistos.setdefault(cod, [])
    if vals in chave: continue  # mesma composição já entrou (preparos que o IBGE repetiu)
    chave.append(vals)
    nome = bonito(r[1])
    p = str(r[3]).strip().upper()
    if prep != 99 and p not in ("NAO SE APLICA", "NAN"):
        nome = f"{nome}, {bonito(p).lower()}"
    itens.append({"codigo": f"{cod}-{prep}", "nome": nome[:120], "kcal": vals[0], "proteina": vals[1], "carboidrato": vals[2], "gordura": vals[3], "fibra": vals[4]})
json.dump(itens, open(out, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
print(len(itens), "itens")
for it in itens[:8] + itens[600:606] + itens[-4:]: print(it["nome"], it["kcal"], it["proteina"], it["carboidrato"], it["gordura"])
