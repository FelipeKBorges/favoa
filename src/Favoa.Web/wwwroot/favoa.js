// Pequenos ajudantes de teclado e foco para o Fávoa.
window.favoaTema = {
    // pref: "escuro" | "claro" | "auto". Guarda neste aparelho e aplica na hora.
    definir(pref) {
        try { localStorage.setItem("favoa_tema", pref); } catch { }
        const r = pref === "auto" ? (window.matchMedia("(prefers-color-scheme: light)").matches ? "claro" : "escuro") : pref;
        document.documentElement.dataset.tema = r;
        const meta = document.querySelector('meta[name="theme-color"]');
        if (meta) meta.setAttribute("content", r === "claro" ? "#FBF6EC" : "#15110C");
    },
    atual() { try { return localStorage.getItem("favoa_tema") || "escuro"; } catch { return "escuro"; } },
};

window.favoa = (() => {
    let handler = null;

    return {
        // Ctrl+K (ou Cmd+K no Mac) abre a captura universal de qualquer lugar.
        registrarAtalho(ref) {
            if (handler) document.removeEventListener("keydown", handler);
            handler = (e) => {
                if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === "k") {
                    e.preventDefault();
                    ref.invokeMethodAsync("AbrirPeloAtalho");
                }
            };
            document.addEventListener("keydown", handler);
        },

        // Máscara de dinheiro: só dígitos, os dois últimos são centavos (4590 → 45,90; 123456 → 1.234,56).
        // Formata antes do Blazor ler o evento (ele escuta no documento; este ouvinte está no próprio campo).
        mascaraMoeda(id) {
            const el = document.getElementById(id);
            if (!el || el.dataset.mascara) return;
            el.dataset.mascara = "1";
            el.addEventListener("input", () => {
                const digitos = el.value.replace(/\D/g, "").replace(/^0+/, "").slice(0, 11);
                if (!digitos) { el.value = ""; return; }
                const c = digitos.padStart(3, "0");
                const inteiro = c.slice(0, -2).replace(/\B(?=(\d{3})+(?!\d))/g, ".");
                el.value = `${inteiro},${c.slice(-2)}`;
            });
        },

        // Celular ou janela estreita: o calendário abre na semana.
        telaEstreita() {
            return window.matchMedia("(max-width: 760px)").matches;
        },

        // Este aparelho já deu permissão para notificações?
        notificacaoPermitida() {
            return "Notification" in window && Notification.permission === "granted";
        },

        removerAtalho() {
            if (handler) document.removeEventListener("keydown", handler);
            handler = null;
        },

        // Recebe o arquivo gerado no servidor e dispara o download no navegador.
        async baixar(nome, tipo, streamRef) {
            const buffer = await streamRef.arrayBuffer();
            const url = URL.createObjectURL(new Blob([buffer], { type: tipo }));
            const a = document.createElement("a");
            a.href = url;
            a.download = nome;
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        },

        // Notificações (Web Push). O servidor guarda a inscrição e envia os lembretes.
        push: {
            async estado() {
                if (!window.isSecureContext) return { estado: "inseguro", endpoint: null };
                const ios = /iPhone|iPad|iPod/.test(navigator.userAgent);
                const instalado = window.navigator.standalone === true || matchMedia("(display-mode: standalone)").matches;
                if (!("serviceWorker" in navigator) || !("PushManager" in window) || !("Notification" in window))
                    return { estado: ios && !instalado ? "ios-instalar" : "sem-suporte", endpoint: null };
                if (Notification.permission === "denied") return { estado: "negado", endpoint: null };
                const reg = await navigator.serviceWorker.ready;
                const sub = await reg.pushManager.getSubscription();
                return { estado: sub && Notification.permission === "granted" ? "ativo" : "inativo", endpoint: sub ? sub.endpoint : null };
            },

            async ativar(chavePublica) {
                try {
                    const permissao = await Notification.requestPermission();
                    if (permissao !== "granted") return { erro: "Sem permissão para notificações neste aparelho." };
                    const reg = await navigator.serviceWorker.ready;
                    let sub = await reg.pushManager.getSubscription();
                    const chave = Uint8Array.from(atob(chavePublica.replace(/-/g, "+").replace(/_/g, "/") + "===".slice((chavePublica.length + 3) % 4)), c => c.charCodeAt(0));
                    // Inscrição feita com outra chave (outro PC, banco novo): refaz.
                    if (sub && sub.options && sub.options.applicationServerKey) {
                        const atual = new Uint8Array(sub.options.applicationServerKey);
                        if (atual.length !== chave.length || atual.some((b, i) => b !== chave[i])) { await sub.unsubscribe(); sub = null; }
                    }
                    if (!sub) sub = await reg.pushManager.subscribe({ userVisibleOnly: true, applicationServerKey: chave });
                    const json = sub.toJSON();
                    return { erro: null, endpoint: json.endpoint, p256dh: json.keys.p256dh, auth: json.keys.auth, aparelho: navigator.userAgent };
                } catch (e) {
                    return { erro: "Não deu para ativar: " + (e && e.message ? e.message : e) };
                }
            },

            async desativar() {
                const reg = await navigator.serviceWorker.ready;
                const sub = await reg.pushManager.getSubscription();
                if (!sub) return null;
                const endpoint = sub.endpoint;
                await sub.unsubscribe();
                return endpoint;
            },
        },

        focar(id) {
            requestAnimationFrame(() => {
                const el = document.getElementById(id);
                if (!el) return;
                el.focus();
                if (el.setSelectionRange) el.setSelectionRange(el.value.length, el.value.length);
            });
        },
    };
})();

// Instalação como app: o service worker só funciona em HTTPS (ex.: Tailscale) ou no localhost.
if ("serviceWorker" in navigator && window.isSecureContext) {
    navigator.serviceWorker.register("/sw.js").catch(() => { /* sem app instalável; o site segue normal */ });
}
