Duas opções, com o .NET e o FFmpeg já dentro (não precisa instalar mais nada):

- **TudoMKV-…-setup.exe** — instala em `%LOCALAPPDATA%\Programs\TudoMKV`, cria o atalho no Menu Iniciar e a entrada de desinstalação em Aplicativos e recursos. Não pede administrador. O Windows pode avisar que o aplicativo não é reconhecido, porque ele ainda não tem assinatura digital: clique em "Mais informações" e "Executar assim mesmo".
- **TudoMKV-…-win-x64.zip** — versão portátil: extraia a pasta e abra `TudoMKV.exe`.

Como usar
---------

Funciona como o MakeMKV: em Arquivo > Abrir arquivo, escolha o `VIDEO_TS.IFO` (DVD), o `index.bdmv` ou o `MovieObject.bdmv` (Blu-ray) ou vídeos comuns. Marque títulos e faixas na árvore e clique em Make MKV.

O MKV sai marcado como gravado pelo TudoMKV, com a data, os nomes das faixas de áudio (Stereo, Surround 5.1…) e o ID de cada faixa no disco de origem, como o MediaInfo mostra nos arquivos do MakeMKV.

Novidades desta versão (desde a v0.4.6)
---------------------------------------

- **Áudio mono vira estéreo 2.0**: em DVD e Blu-ray, cada faixa mono ganha uma alternativa estéreo marcável na árvore. Ao gravar, o áudio mono é duplicado canal a canal (sem a perda de volume do downmix comum) e a faixa estéreo AC-3 192 kbps sai como padrão, com o mono original preservado.
- **Metadados deixam de se repetir entre discos**: as informações do IMDb agora pertencem ao disco aberto. Sem uma consulta verificada, nenhum dado de filme é gravado — antes, um disco sem IMDb informado saía com os dados do disco anterior.
- **Nada de `tags.xml` na pasta do aplicativo**: as etiquetas são montadas na hora, a partir do retorno da OMDb, e passam pelo `mkvpropedit` por um arquivo temporário do sistema, apagado em seguida.

Discos com proteção contra cópia (CSS, AACS) não são suportados. Veja o LEIA-ME.txt dentro do pacote.

Contribuidores
--------------

- Clediomar do Carmo Santos
- Trae (assistente de IA), no código, nos testes e no empacotamento

Versão anterior: [v0.4.6](https://github.com/Hayllander/TudoMKV/releases/tag/v0.4.6)
