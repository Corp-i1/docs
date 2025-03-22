document.addEventListener('DOMContentLoaded', function () {
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