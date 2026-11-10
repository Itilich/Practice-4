(() => {
    const connection = new signalR.HubConnectionBuilder().withUrl('/game').withAutomaticReconnect().build();
    const $ = id => document.getElementById(id);
    let state = null;
    let myMark = null;

    function showNotice(message, error = false) {
        $('notice').textContent = message || '';
        $('notice').classList.toggle('error', error);
    }
    function updateState(next) {
        if (next.playerMark) myMark = next.playerMark;
        state = next;
        $('game-area').hidden = false;
        $('room-label').textContent = next.code;
        $('board').style.setProperty('--size', next.size);
        $('board').replaceChildren();
        next.board.forEach((mark, index) => {
            const button = document.createElement('button');
            button.type = 'button';
            button.className = `cell ${mark ? `mark-${mark.toLowerCase()}` : ''}`;
            button.textContent = mark;
            button.setAttribute('role', 'gridcell');
            button.setAttribute('aria-label', `Строка ${Math.floor(index / next.size) + 1}, столбец ${index % next.size + 1}${mark ? `: ${mark}` : ''}`);
            button.disabled = Boolean(mark) || next.status !== 'playing' || next.currentPlayer !== myMark || connection.state !== signalR.HubConnectionState.Connected;
            button.addEventListener('click', () => connection.invoke('MakeMove', next.code, Math.floor(index / next.size), index % next.size).catch(e => showNotice(e.message, true)));
            $('board').append(button);
        });
        const status = $('status');
        status.classList.toggle('finished', next.status !== 'playing');
        if (next.winner) status.textContent = `Победили ${next.winner}!`;
        else if (next.status === 'draw') status.textContent = 'Ничья!';
        else status.textContent = next.currentPlayer === myMark
            ? `Ваш ход — вы играете за ${myMark}`
            : `Ход соперника (${next.currentPlayer}) — вы играете за ${myMark}`;
    }

    connection.on('GameCreated', updateState);
    connection.on('GameJoined', updateState);
    connection.on('StateUpdated', updateState);
    connection.on('PlayerJoined', message => showNotice(message));
    connection.on('MoveAccepted', () => showNotice('Ход сохранён.'));
    connection.on('ResetAccepted', () => showNotice('Новая партия началась.'));
    connection.onreconnecting(() => { $('connection-text').textContent = 'Переподключение…'; $('connection-dot').className = 'offline'; });
    connection.onreconnected(() => { $('connection-text').textContent = 'Подключено'; $('connection-dot').className = 'online'; });

    connection.start().then(() => {
        $('connection-text').textContent = 'Подключено к серверу';
        $('connection-dot').className = 'online';
        $('create').disabled = false;
        $('join').disabled = false;
    }).catch(error => {
        $('connection-text').textContent = 'Не удалось подключиться';
        $('connection-dot').className = 'offline';
        showNotice(error.message, true);
    });
    $('create').disabled = true;
    $('join').disabled = true;
    $('create').addEventListener('click', async () => {
        showNotice('');
        try { await connection.invoke('CreateGame', Number($('size').value)); }
        catch (e) { showNotice(e.message, true); }
    });
    $('join').addEventListener('click', async () => {
        showNotice('');
        try { await connection.invoke('JoinGame', $('room-code').value); }
        catch (e) { showNotice(e.message, true); }
    });
    $('room-code').addEventListener('keydown', event => { if (event.key === 'Enter') $('join').click(); });
    $('reset').addEventListener('click', () => state && connection.invoke('ResetGame', state.code).catch(e => showNotice(e.message, true)));
    $('copy-code').addEventListener('click', async () => {
        if (!state) return;
        try { await navigator.clipboard.writeText(state.code); showNotice('Код скопирован.'); }
        catch { showNotice(`Код комнаты: ${state.code}`); }
    });
})();
