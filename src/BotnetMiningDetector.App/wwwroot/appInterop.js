// instala el puente con la aplicacion
(function () {
  if (window.agentBridgeInstalled) {
    return;
  }

  window.agentBridgeInstalled = true;

  // envia un mensaje a la aplicacion
  function post(message) {
    if (!window.chrome || !window.chrome.webview) {
      return false;
    }

    window.chrome.webview.postMessage(message);
    return true;
  }

  // procesa clics de la interfaz
  document.addEventListener(
    "click",
    function (event) {
      const element = event.target.closest(
        "[data-agent-nav],[data-agent-action],[data-agent-scenario],[data-agent-alert]",
      );
      if (!element) {
        return;
      }

      const message = {};
      if (element.dataset.agentNav) {
        message.kind = "nav";
        message.value = element.dataset.agentNav;
      } else if (element.dataset.agentScenario) {
        message.kind = "scenario";
        message.value = element.dataset.agentScenario;
      } else if (element.dataset.agentAlert) {
        message.kind = "alert";
        message.value = element.dataset.agentAlert;
      } else if (element.dataset.agentAction) {
        message.kind = "action";
        message.value = element.dataset.agentAction;
        if (element.dataset.agentAlertId) {
          message.alertId = element.dataset.agentAlertId;
        }
      }

      if (message.kind && post(message)) {
        event.preventDefault();
        event.stopImmediatePropagation();
      }
    },
    true,
  );

  // procesa cambios de filtros
  document.addEventListener(
    "input",
    function (event) {
      const element = event.target.closest("[data-agent-filter]");
      if (!element) {
        return;
      }

      if (
        post({
          kind: "filter",
          name: element.dataset.agentFilter,
          value: String(element.value || ""),
        })
      ) {
        event.stopImmediatePropagation();
      }
    },
    true,
  );

  // procesa cambios de controles
  document.addEventListener(
    "change",
    function (event) {
      const element = event.target.closest(
        "[data-agent-setting],[data-agent-filter],[data-agent-column]",
      );
      if (!element) {
        return;
      }

      const value =
        element.type === "checkbox" ? element.checked : element.value;
      const message = {};
      if (element.dataset.agentSetting) {
        message.kind = "setting";
        message.name = element.dataset.agentSetting;
      } else if (element.dataset.agentFilter) {
        message.kind = "filter";
        message.name = element.dataset.agentFilter;
      } else if (element.dataset.agentColumn) {
        message.kind = "column";
        message.name = element.dataset.agentColumn;
      }

      message.value = String(value);
      if (message.kind && post(message)) {
        event.stopImmediatePropagation();
      }
    },
    true,
  );

  // busca la tabla desplazable
  function findScrollableTable(event) {
    const target =
      event.target && event.target.closest
        ? event.target
        : document.elementFromPoint(event.clientX, event.clientY);
    const directMatch = target ? target.closest(".table-wrap") : null;

    if (directMatch) {
      return directMatch;
    }

    const pointedElement = document.elementFromPoint(
      event.clientX,
      event.clientY,
    );
    return pointedElement && pointedElement.closest
      ? pointedElement.closest(".table-wrap")
      : null;
  }

  // calcula la escala del desplazamiento
  function getWheelScale(event, element) {
    if (event.deltaMode === WheelEvent.DOM_DELTA_LINE) {
      return 16;
    }

    if (event.deltaMode === WheelEvent.DOM_DELTA_PAGE) {
      return element.clientWidth;
    }

    return 1;
  }

  // obtiene el desplazamiento horizontal
  function getHorizontalWheelDelta(event, element) {
    const scale = getWheelScale(event, element);
    const standardDelta = Number.isFinite(event.deltaX)
      ? event.deltaX * scale
      : 0;
    const legacyDelta = Number.isFinite(event.wheelDeltaX)
      ? -event.wheelDeltaX
      : 0;
    let horizontalDelta =
      Math.abs(standardDelta) >= Math.abs(legacyDelta)
        ? standardDelta
        : legacyDelta;

    if (Math.abs(horizontalDelta) < 0.01 && event.shiftKey) {
      horizontalDelta =
        (Number.isFinite(event.deltaY) ? event.deltaY : -event.wheelDelta) *
        scale;
    }

    return Number.isFinite(horizontalDelta) ? horizontalDelta : 0;
  }

  // mueve las tablas en horizontal
  function handleHorizontalTableWheel(event) {
    const tableWrap = findScrollableTable(event);
    if (!tableWrap || tableWrap.scrollWidth <= tableWrap.clientWidth) {
      return;
    }

    const horizontalDelta = getHorizontalWheelDelta(event, tableWrap);
    if (Math.abs(horizontalDelta) < 0.01) {
      return;
    }

    const previousScrollLeft = tableWrap.scrollLeft;
    tableWrap.scrollLeft += horizontalDelta;
    if (tableWrap.scrollLeft !== previousScrollLeft) {
      event.preventDefault();
      event.stopPropagation();
    }
  }

  document.addEventListener("wheel", handleHorizontalTableWheel, {
    capture: true,
    passive: false,
  });
  document.addEventListener("mousewheel", handleHorizontalTableWheel, {
    capture: true,
    passive: false,
  });
})();
