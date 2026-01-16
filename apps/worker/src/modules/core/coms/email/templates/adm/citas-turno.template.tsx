import { CitasTurnoTemplateDto } from '@hsm-lib/definitions/dtos';

export function CitasTurnoTemplate(
  data: CitasTurnoTemplateDto,
): React.ReactNode {
  return (
    <>
      <p>
        Estimad@ paciente: <i>{data.patientName}</i>
        <br />
        Le saludamos del Hospital Santamaria, para recordarle su cita:
        <br />
        <br />
        Dia: <b>{data.appointmentDate}</b>
        <br />
        Hora: <b>{data.appointmentTime}</b>
        <br />
        Turno: <b>{data.appointmentDetails}</b>
        <br />
        Especialidad: <b>{data.medicalSpecialty}</b>
        <br />
        Tipo: <b>{data.appointmentType}</b>
        <br />
        Nota: <b>{data.appointmentNote}</b>
        <br />
        <br />
        Atentamente,
        <br />
        Servicio al Paciente
        <br />
        Celular 0981764904
      </p>
    </>
  );
}
