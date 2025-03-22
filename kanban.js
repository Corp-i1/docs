document.addEventListener('DOMContentLoaded', function () {
    const style = document.createElement('style');
    style.textContent = `
        .kanban-board {
            display: flex;
            gap: 10px;
        }
        .column {
            border: 1px solid #ccc;
            padding: 10px;
            width: 200px;
        }
        .cards {
            min-height: 100px;
        }
        .card {
            background: #f0f0f0;
            margin: 5px 0;
            padding: 10px;
            cursor: move;
        }
    `;
    document.head.appendChild(style);

    const kanbanBoard = document.createElement('div');
    kanbanBoard.className = 'kanban-board';
    kanbanBoard.innerHTML = `
        <div class="column">
            <h3>To Do</h3>
            <div class="cards" id="todo-cards"></div>
            <button class="add-card">Add Card</button>
        </div>
        <div class="column">
            <h3>In Progress</h3>
            <div class="cards" id="inprogress-cards"></div>
            <button class="add-card">Add Card</button>
        </div>
        <div class="column">
            <h3>Done</h3>
            <div class="cards" id="done-cards"></div>
            <button class="add-card">Add Card</button>
        </div>
    `;
    document.body.appendChild(kanbanBoard);

    const addCardButtons = document.querySelectorAll('.add-card');
    const columns = document.querySelectorAll('.column');

    addCardButtons.forEach(button => {
        button.addEventListener('click', function () {
            const column = this.parentElement;
            const cardText = prompt('Enter card text:');
            if (cardText) {
                const card = document.createElement('div');
                card.className = 'card';
                card.textContent = cardText;
                card.draggable = true;
                card.id = 'card-' + Date.now();
                column.querySelector('.cards').appendChild(card);
            }
        });
    });

    columns.forEach(column => {
        column.addEventListener('dragover', function (e) {
            e.preventDefault();
        });

        column.addEventListener('drop', function (e) {
            const cardId = e.dataTransfer.getData('text');
            const card = document.getElementById(cardId);
            column.querySelector('.cards').appendChild(card);
        });
    });

    document.addEventListener('dragstart', function (e) {
        if (e.target.classList.contains('card')) {
            e.dataTransfer.setData('text', e.target.id);
        }
    });
});