const state = {
  meetings: [],
  people: []
};

const meetingsList = document.querySelector("#meetings-list");
const peopleList = document.querySelector("#people-list");

document.querySelectorAll(".tab").forEach((tab) => {
  tab.addEventListener("click", () => {
    document.querySelectorAll(".tab").forEach((item) => item.classList.remove("active"));
    document.querySelectorAll(".view").forEach((item) => item.classList.remove("active"));
    tab.classList.add("active");
    document.querySelector(`#${tab.dataset.view}-view`).classList.add("active");
  });
});

document.querySelector("#refresh-meetings").addEventListener("click", loadMeetings);
document.querySelector("#refresh-people").addEventListener("click", loadPeople);

document.querySelector("#person-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const form = new FormData(event.currentTarget);
  await fetch("/api/people", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(Object.fromEntries(form.entries()))
  });
  event.currentTarget.reset();
  await loadPeople();
});

async function loadMeetings() {
  const response = await fetch("/api/meetings");
  state.meetings = await response.json();
  renderMeetings();
}

async function loadPeople() {
  const response = await fetch("/api/people");
  state.people = await response.json();
  renderPeople();
}

function renderMeetings() {
  if (state.meetings.length === 0) {
    meetingsList.innerHTML = `<div class="empty">No meeting candidates yet.</div>`;
    return;
  }

  meetingsList.innerHTML = state.meetings.map((meeting) => `
    <article class="meeting" data-id="${meeting.id}">
      <div class="meeting-header">
        <div class="meeting-title">
          <h3>${escapeHtml(meeting.topic || "Untitled meeting")}</h3>
          <div class="muted">${escapeHtml(meeting.chatTitle || meeting.chatId)} · confidence ${Math.round((meeting.confidence || 0) * 100)}%</div>
        </div>
        <span class="status">${escapeHtml(meeting.status)}</span>
      </div>

      <div class="fields">
        <label>Topic
          <input data-field="topic" value="${escapeAttribute(meeting.topic || "")}">
        </label>
        <label>Time
          <input data-field="proposedStartUtc" type="datetime-local" value="${toLocalInput(meeting.proposedStartUtc)}">
        </label>
        <label>Meeting URL
          <input data-field="meetingUrl" value="${escapeAttribute(meeting.meetingUrl || "")}">
        </label>
      </div>

      <div class="participants">
        ${meeting.participants.map(renderParticipant).join("") || `<span class="muted">No participants detected.</span>`}
      </div>

      <form class="inline-form participant-form">
        <input name="telegramUsername" placeholder="@telegram">
        <input name="displayName" placeholder="Name">
        <input name="email" placeholder="email@company.com" type="email">
        <button type="submit">Add person</button>
      </form>

      <div class="actions">
        <button class="secondary" data-action="save">Save</button>
        <button class="ok" data-action="approve">Confirm</button>
        <button class="danger" data-action="cancel">Cancel</button>
      </div>

      ${meeting.aiReason ? `<p class="muted">${escapeHtml(meeting.aiReason)}</p>` : ""}
    </article>
  `).join("");

  meetingsList.querySelectorAll("[data-action]").forEach((button) => {
    button.addEventListener("click", () => handleMeetingAction(button.closest(".meeting"), button.dataset.action));
  });

  meetingsList.querySelectorAll(".participant-form").forEach((form) => {
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      const meeting = form.closest(".meeting");
      await fetch(`/api/meetings/${meeting.dataset.id}/participants`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(Object.fromEntries(new FormData(form).entries()))
      });
      await loadMeetings();
    });
  });
}

function renderParticipant(participant) {
  const name = participant.displayName || participant.telegramUsername || participant.telegramUserId || "Unknown";
  const email = participant.email || "email missing";
  return `
    <div class="participant">
      <strong>${escapeHtml(String(name))}</strong>
      <div class="muted">${escapeHtml(email)}</div>
      <div class="muted">${escapeHtml(participant.response)} · ${escapeHtml(participant.role)}</div>
    </div>
  `;
}

async function handleMeetingAction(element, action) {
  const id = element.dataset.id;
  if (action === "save") {
    const payload = {};
    element.querySelectorAll("[data-field]").forEach((input) => {
      payload[input.dataset.field] = input.dataset.field === "proposedStartUtc" && input.value
        ? new Date(input.value).toISOString()
        : input.value;
    });

    await fetch(`/api/meetings/${id}`, {
      method: "PATCH",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
  } else {
    await fetch(`/api/meetings/${id}/${action}`, { method: "POST" });
  }

  await loadMeetings();
}

function renderPeople() {
  if (state.people.length === 0) {
    peopleList.innerHTML = `<div class="empty">No Telegram users detected yet.</div>`;
    return;
  }

  peopleList.innerHTML = state.people.map((person) => `
    <div class="row" data-id="${person.id}">
      <input data-field="telegramUsername" value="${escapeAttribute(person.telegramUsername ? `@${person.telegramUsername}` : "")}" placeholder="@telegram">
      <input data-field="displayName" value="${escapeAttribute(person.displayName || "")}" placeholder="Name">
      <input data-field="email" value="${escapeAttribute(person.email || "")}" placeholder="email@company.com">
      <button data-action="save-person">Save</button>
    </div>
  `).join("");

  peopleList.querySelectorAll("[data-action='save-person']").forEach((button) => {
    button.addEventListener("click", async () => {
      const row = button.closest(".row");
      const payload = {};
      row.querySelectorAll("[data-field]").forEach((input) => payload[input.dataset.field] = input.value);
      await fetch(`/api/people/${row.dataset.id}`, {
        method: "PATCH",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload)
      });
      await loadPeople();
    });
  });
}

function toLocalInput(value) {
  if (!value) {
    return "";
  }

  const date = new Date(value);
  const offset = date.getTimezoneOffset();
  const local = new Date(date.getTime() - offset * 60 * 1000);
  return local.toISOString().slice(0, 16);
}

function escapeHtml(value) {
  return String(value)
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

function escapeAttribute(value) {
  return escapeHtml(value);
}

loadMeetings();
loadPeople();
