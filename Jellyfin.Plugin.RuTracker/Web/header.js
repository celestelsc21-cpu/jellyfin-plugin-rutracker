/*
 * RuTracker plugin: adds a button to the Jellyfin web client header,
 * next to the search button. Shown only to users with the Search role.
 * Never throws into the host page: any failure simply hides the button.
 */
(function () {
    'use strict';

    if (window.__ruTrackerHeaderLoaded) {
        return;
    }
    window.__ruTrackerHeaderLoaded = true;

    var BUTTON_CLASS = 'headerRuTrackerButton';
    var ICON_PATH = 'M19.3 16.9c.4-.7.7-1.5.7-2.4 0-2.5-2-4.5-4.5-4.5S11 12 11 14.5s2 4.5 4.5 4.5c.9 0 1.7-.3 2.4-.7l3.2 3.2 1.4-1.4-3.2-3.2zm-3.8.1c-1.4 0-2.5-1.1-2.5-2.5s1.1-2.5 2.5-2.5 2.5 1.1 2.5 2.5-1.1 2.5-2.5 2.5zM12 20v2C6.48 22 2 17.52 2 12S6.48 2 12 2c4.84 0 8.87 3.44 9.8 8h-2.07c-.64-2.46-2.4-4.47-4.73-5.41V5c0 1.1-.9 2-2 2h-2v2c0 .55-.45 1-1 1H8v2h2v3H9l-4.79-4.79C4.08 10.79 4 11.38 4 12c0 4.41 3.59 8 8 8z';

    // header.js lives at <base>/RuTracker/Web/header.js; the page is <base>/RuTracker/Web/
    var pageUrl = (function () {
        try {
            return new URL('./', document.currentScript.src).href;
        } catch (e) {
            return null;
        }
    })();

    var access = { userId: null, known: false, allowed: false, pending: null };

    function currentUserId() {
        try {
            return window.ApiClient && typeof window.ApiClient.getCurrentUserId === 'function'
                ? window.ApiClient.getCurrentUserId()
                : null;
        } catch (e) {
            return null;
        }
    }

    function checkAccess() {
        var userId = currentUserId();
        if (!userId) {
            access = { userId: null, known: false, allowed: false, pending: null };
            return Promise.resolve(false);
        }
        if (access.userId === userId && access.known) {
            return Promise.resolve(access.allowed);
        }
        if (access.userId === userId && access.pending) {
            return access.pending;
        }

        access = { userId: userId, known: false, allowed: false, pending: null };
        var api = window.ApiClient;
        access.pending = api.getJSON(api.getUrl('RuTracker/Access/Me')).then(function (info) {
            if (access.userId === userId) {
                access.allowed = !!(info && info.CanSearch);
                access.known = true;
                access.pending = null;
            }
            return !!(info && info.CanSearch);
        }, function () {
            if (access.userId === userId) {
                access.allowed = false;
                access.known = true;
                access.pending = null;
            }
            return false;
        });
        return access.pending;
    }

    function createButton() {
        var button = document.createElement('button');
        button.type = 'button';
        button.setAttribute('is', 'paper-icon-button-light');
        button.className = 'headerButton headerButtonRight paper-icon-button-light ' + BUTTON_CLASS;
        button.title = 'RuTracker';
        button.setAttribute('aria-label', 'RuTracker');

        var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        svg.setAttribute('viewBox', '0 0 24 24');
        svg.setAttribute('width', '24');
        svg.setAttribute('height', '24');
        svg.setAttribute('aria-hidden', 'true');
        svg.style.fill = 'currentColor';
        svg.style.verticalAlign = 'middle';
        var path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
        path.setAttribute('d', ICON_PATH);
        svg.appendChild(path);
        button.appendChild(svg);

        button.addEventListener('click', function () {
            if (pageUrl) {
                window.location.href = pageUrl;
            }
        });
        return button;
    }

    function update() {
        var headerRight = document.querySelector('.skinHeader .headerRight');
        if (!headerRight || !pageUrl) {
            return;
        }

        checkAccess().then(function (allowed) {
            var existing = document.querySelector('.' + BUTTON_CLASS);
            if (!allowed) {
                if (existing) {
                    existing.remove();
                }
                return;
            }
            if (existing && existing.parentNode === headerRight) {
                return;
            }
            if (existing) {
                existing.remove();
            }
            var searchButton = headerRight.querySelector('.headerSearchButton');
            headerRight.insertBefore(createButton(), searchButton || null);
        }).catch(function () { /* keep the host page intact */ });
    }

    var scheduled = false;
    function schedule() {
        if (scheduled) {
            return;
        }
        scheduled = true;
        setTimeout(function () {
            scheduled = false;
            update();
        }, 300);
    }

    // The header is rendered by the SPA and may be re-rendered (login, user switch).
    try {
        new MutationObserver(schedule).observe(document.body, { childList: true, subtree: true });
    } catch (e) { /* very old browser: rely on events below */ }
    document.addEventListener('viewshow', schedule);
    window.addEventListener('hashchange', schedule);
    schedule();
})();
