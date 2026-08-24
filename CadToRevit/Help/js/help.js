(function () {
  const searchBox = document.getElementById('searchBox');
  const topics = Array.from(document.querySelectorAll('.topic'));
  const links = Array.from(document.querySelectorAll('#helpNav a'));
  const emptyState = document.getElementById('emptyState');

  function normalize(value) {
    return (value || '').toLowerCase().replace(/\s+/g, ' ').trim();
  }

  function filterTopics() {
    const query = normalize(searchBox.value);
    let visibleCount = 0;

    topics.forEach(topic => {
      const haystack = normalize(topic.textContent + ' ' + (topic.dataset.search || ''));
      const visible = !query || haystack.includes(query);
      topic.classList.toggle('search-hidden', !visible);
      if (visible) visibleCount += 1;
    });

    emptyState.hidden = visibleCount !== 0;
  }

  function updateActiveLink() {
    const current = window.location.hash || '#getting-started';
    links.forEach(link => link.classList.toggle('active', link.getAttribute('href') === current));
  }

  searchBox.addEventListener('input', filterTopics);
  window.addEventListener('hashchange', updateActiveLink);
  updateActiveLink();
})();
