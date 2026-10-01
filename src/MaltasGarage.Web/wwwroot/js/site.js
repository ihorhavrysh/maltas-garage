// Malta's Garage - Main JavaScript

document.addEventListener('DOMContentLoaded', function () {
    // Initialize tooltips
    var tooltipTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="tooltip"]'));
    tooltipTriggerList.forEach(function (tooltipTriggerEl) {
        new bootstrap.Tooltip(tooltipTriggerEl);
    });

    // Initialize popovers
    var popoverTriggerList = [].slice.call(document.querySelectorAll('[data-bs-toggle="popover"]'));
    popoverTriggerList.forEach(function (popoverTriggerEl) {
        new bootstrap.Popover(popoverTriggerEl);
    });

    // Header search (desktop and mobile copies)
    initSearchAutocomplete(document.getElementById('q-top'));
    initSearchAutocomplete(document.getElementById('q-mob'));
});

// Search autocomplete
function initSearchAutocomplete(inputEl, opts) {
    if (!inputEl) return;
    opts = opts || {};

    var debounceTimer;
    var dropdown = null;

    var container = inputEl.closest('.input-group') || inputEl.parentElement;
    if (getComputedStyle(container).position === 'static') container.style.position = 'relative';

    function escHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function hide() {
        if (dropdown) { dropdown.remove(); dropdown = null; }
    }

    function render(items, q) {
        hide();
        dropdown = document.createElement('ul');
        dropdown.className = 'list-group position-absolute w-100 shadow';
        dropdown.style.cssText = 'z-index:1055;top:100%;left:0;';

        if (!items.length) {
            var empty = document.createElement('li');
            empty.className = 'list-group-item text-muted small py-2';
            empty.textContent = 'No results found';
            dropdown.appendChild(empty);
        } else {
            items.forEach(function (item) {
                var li = document.createElement('li');
                li.className = 'list-group-item list-group-item-action d-flex align-items-center gap-3 py-2';
                li.style.cursor = 'pointer';

                var img = item.imageUrl
                    ? '<img src="' + escHtml(item.imageUrl) + '" style="width:48px;height:48px;object-fit:cover;border-radius:4px;flex-shrink:0;" alt="">'
                    : '<div style="width:48px;height:48px;background:#f0f0f0;border-radius:4px;flex-shrink:0;display:flex;align-items:center;justify-content:center;"><i class="bi bi-image text-muted"></i></div>';

                li.innerHTML = img +
                    '<div class="flex-grow-1 overflow-hidden">' +
                    '<div class="text-truncate fw-semibold small">' + escHtml(item.title) + '</div>' +
                    '<div style="font-size:.75rem;">' +
                    '<span class="badge bg-secondary me-1">' + escHtml(item.category) + '</span>' +
                    '\u20AC' + parseFloat(item.price).toFixed(2) +
                    '</div></div>';

                li.addEventListener('mousedown', function (e) {
                    e.preventDefault();
                    window.location.href = '/Listing/' + item.id;
                });

                dropdown.appendChild(li);
            });

            var footer = document.createElement('li');
            footer.className = 'list-group-item list-group-item-action text-center small text-primary py-2';
            footer.style.cursor = 'pointer';
            footer.innerHTML = '<i class="bi bi-search me-1"></i>View all results for &ldquo;' + escHtml(q) + '&rdquo;';
            footer.addEventListener('mousedown', function (e) {
                e.preventDefault();
                if (inputEl.form) inputEl.form.submit();
            });
            dropdown.appendChild(footer);
        }

        container.appendChild(dropdown);
    }

    async function fetchAndRender(q) {
        var url = '/Api/SearchSuggestions?q=' + encodeURIComponent(q);
        if (opts.categoryId) url += '&categoryId=' + opts.categoryId;
        try {
            var resp = await fetch(url);
            if (!resp.ok) return;
            var items = await resp.json();
            if (inputEl.value.trim() === q) render(items, q);
        } catch (e) { }
    }

    inputEl.addEventListener('input', function () {
        clearTimeout(debounceTimer);
        var val = this.value.trim();
        if (val.length < 3) { hide(); return; }
        debounceTimer = setTimeout(function () { fetchAndRender(val); }, 300);
    });

    inputEl.addEventListener('keydown', function (e) {
        if (e.key === 'Escape') hide();
    });

    inputEl.addEventListener('blur', function () {
        setTimeout(hide, 150);
    });
}

// Utility functions
const MaltasGarage = {
    formatPrice: function (amount, currency = '€') {
        return currency + parseFloat(amount).toFixed(2);
    },

    formatDate: function (date) {
        return new Date(date).toLocaleDateString('en-GB', {
            day: 'numeric',
            month: 'short',
            year: 'numeric'
        });
    },

    daysRemaining: function (endDate) {
        const now = new Date();
        const end = new Date(endDate);
        const diff = Math.ceil((end - now) / (1000 * 60 * 60 * 24));
        return diff > 0 ? diff : 0;
    },

    showToast: function (message, type = 'info') {
        console.log(`[${type}] ${message}`);
    }
};
